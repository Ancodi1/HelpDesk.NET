using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    [Theory]
    [InlineData(true, false, UserRole.Technician)]
    [InlineData(true, true, UserRole.Technician)]
    [InlineData(false, false, UserRole.Technician)]
    [InlineData(false, true, UserRole.Technician)]
    [InlineData(true, false, UserRole.Admin)]
    [InlineData(true, true, UserRole.Admin)]
    [InlineData(false, false, UserRole.Admin)]
    [InlineData(false, true, UserRole.Admin)]
    public async Task AdminRoleChangeAndAssignment_SerializeBothOrders(bool assignmentFirst, bool reassign, UserRole initialRole)
    {
        var target = await SeedHttpUserAsync(initialRole);
        var oldAssignee = reassign ? await SeedHttpUserAsync(UserRole.Technician) : null;
        var ticket = await CreateTicketAsync(assignedToUserId: oldAssignee?.Id);
        var pause = new PauseRoleOrAssignmentSave(target.Id, ticket.Id, assignmentFirst);
        var probe = new LockCommandProbe(assignmentFirst ? "FOR NO KEY UPDATE" : "FOR SHARE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(probe))));
        using var firstClient = await CreateAdminClientAsync(firstFactory);
        using var secondClient = await CreateAdminClientAsync(secondFactory);
        Task<HttpResponseMessage> Assign(HttpClient client) => client.PatchAsJsonAsync(
            $"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = target.Id });
        Task<HttpResponseMessage> Demote(HttpClient client) => client.PutAsJsonAsync(
            $"/api/users/{target.Id}", new { target.Name, target.Email, role = "User" });
        var first = assignmentFirst ? Assign(firstClient) : Demote(firstClient);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = assignmentFirst ? Demote(secondClient) : Assign(secondClient);
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var firstResponse = await first;
            using var secondResponse = await second;
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(assignmentFirst ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, secondResponse.StatusCode);
            if (assignmentFirst) ticket.AssignedToUserId = target.Id;
            else target.Role = UserRole.User;
            await AssertStoredAndReturnedAsync(ticket);
            await AssertUserStoredAsync(new(target.Id, target.Name, target.Email, target.Role, target.CreatedAt));
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await first).Dispose();
            if (second is not null) (await second).Dispose();
        }
    }

    [Fact]
    public async Task TechnicianStatus_WaitsForConcurrentReassignmentAndThenRejectsOldAssignee()
    {
        var technician = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: technician.Id);
        // Esperar después del UPDATE: antes de SaveChanges todavía no se bloquea el ticket.
        var pause = new PauseTicketAfterSave(ticket.Id);
        var probe = new LockCommandProbe("FOR UPDATE");
        using var adminFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var techFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(probe))));
        using var admin = await CreateAdminClientAsync(adminFactory);
        using var tech = CreateAuthenticatedClient(technician, techFactory);
        var assignment = admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = other.Id });
        Task<HttpResponseMessage>? status = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            status = tech.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "InProgress" });
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(status.IsCompleted);
            pause.Resume.TrySetResult();
            using var assignmentResponse = await assignment;
            using var statusResponse = await status;
            Assert.Equal(HttpStatusCode.OK, assignmentResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, statusResponse.StatusCode);
            Assert.Empty(await statusResponse.Content.ReadAsStringAsync());
            ticket.AssignedToUserId = other.Id;
            await AssertStoredAndReturnedAsync(ticket);
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await assignment).Dispose();
            if (status is not null) (await status).Dispose();
        }
    }

    [Fact]
    public async Task StatusLock_CommitsAuthorizedStatusBeforeReassignment_ThenRejectsFormerTechnician()
    {
        var technician = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: technician.Id);
        var pause = new PauseTicketAfterSave(ticket.Id);
        var probe = new LockCommandProbe("UPDATE \"Tickets\"");
        using var techFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var adminFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(probe))));
        using var tech = CreateAuthenticatedClient(technician, techFactory);
        using var admin = await CreateAdminClientAsync(adminFactory);
        var status = tech.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "InProgress" });
        Task<HttpResponseMessage>? assignment = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            assignment = admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = other.Id });
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(assignment.IsCompleted);
            pause.Resume.TrySetResult();
            using var statusResponse = await status;
            using var assignmentResponse = await assignment;
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, assignmentResponse.StatusCode);
            ticket.Status = TicketStatus.InProgress;
            ticket.AssignedToUserId = other.Id;
            await AssertStoredAndReturnedAsync(ticket);

            // Una nueva petición tras perder la asignación no puede modificar el estado.
            using var denied = await tech.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "Resolved" });
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Empty(await denied.Content.ReadAsStringAsync());
            await AssertStoredAndReturnedAsync(ticket);
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await status).Dispose();
            if (assignment is not null) (await assignment).Dispose();
        }
    }

    private sealed class PauseTicketAfterSave(int ticketId) : SaveChangesInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (db.ChangeTracker.Entries<Ticket>().Any(entry => entry.Entity.Id == ticketId))
            {
                // El SQL ya se ejecutó, pero el endpoint aún no ha confirmado la transacción.
                Assert.NotNull(db.Database.CurrentTransaction);
                Reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class PauseRoleOrAssignmentSave(int userId, int ticketId, bool assignmentFirst) : SaveChangesInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (assignmentFirst
                ? db.ChangeTracker.Entries<Ticket>().Any(entry => entry.Entity.Id == ticketId)
                : db.ChangeTracker.Entries<User>().Any(entry => entry.Entity.Id == userId))
            {
                Reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class LockCommandProbe(string marker) : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(marker, StringComparison.Ordinal))
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }
}
