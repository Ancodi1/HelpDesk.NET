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
    [InlineData(true, UserRole.Technician, false)]
    [InlineData(false, UserRole.Technician, false)]
    [InlineData(true, UserRole.Admin, false)]
    [InlineData(false, UserRole.Admin, false)]
    [InlineData(true, UserRole.Technician, true)]
    [InlineData(false, UserRole.Technician, true)]
    [InlineData(true, UserRole.Admin, true)]
    [InlineData(false, UserRole.Admin, true)]
    public async Task AssignmentAndDowngrade_ConcurrentOperations_KeepRoleInvariant(bool assignmentFirst, UserRole initialRole, bool reassignment)
    {
        var user = await SeedHttpUserAsync(initialRole);
        var ticket = await CreateTicketAsync();
        if (reassignment)
        {
            var previous = await SeedHttpUserAsync(UserRole.Technician);
            using var client = fixture.Factory.CreateClient();
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = previous.Id });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ticket.AssignedToUserId = previous.Id;
        }
        var pause = new PauseRelatedSave(assignmentFirst ? ticket.Id : null, assignmentFirst ? null : user.Id);
        var waiting = new ObserveUserLock(assignmentFirst ? "FOR NO KEY UPDATE" : "FOR SHARE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(waiting))));
        using var firstClient = firstFactory.CreateClient();
        using var secondClient = secondFactory.CreateClient();
        Task<HttpResponseMessage> Assign(HttpClient client) => client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = user.Id });
        Task<HttpResponseMessage> Downgrade(HttpClient client) => client.PutAsJsonAsync($"/api/users/{user.Id}", new { user.Name, user.Email, role = "User" });
        var first = assignmentFirst ? Assign(firstClient) : Downgrade(firstClient);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holderPid = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = assignmentFirst ? Downgrade(secondClient) : Assign(secondClient);
            var waiterPid = await waiting.Executing.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiterPid, holderPid);
            Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var firstResponse = await first;
            using var secondResponse = await second;
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(assignmentFirst ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, secondResponse.StatusCode);
            await AssertHasErrorAsync(secondResponse);
            if (assignmentFirst) ticket.AssignedToUserId = user.Id;
            await AssertStoredAndReturnedAsync(ticket);
            await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, assignmentFirst ? initialRole : UserRole.User, user.CreatedAt));
            await AssertNoInvalidAssignmentAsync(user.Id);
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await first).Dispose();
            if (second is not null) (await second).Dispose();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserLock_DoesNotBlockOperationsForAnotherUser(bool assignmentFirst)
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync();
        var pause = new PauseRelatedSave(assignmentFirst ? ticket.Id : null, assignmentFirst ? null : user.Id);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var heldClient = factory.CreateClient();
        using var otherClient = fixture.Factory.CreateClient();
        var held = assignmentFirst
            ? heldClient.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = user.Id })
            : heldClient.PutAsJsonAsync($"/api/users/{user.Id}", new { user.Name, user.Email, role = "User" });
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var otherTicket = await CreateTicketAsync();
            using var response = await (assignmentFirst
                ? otherClient.PutAsJsonAsync($"/api/users/{other.Id}", new { other.Name, other.Email, role = "User" })
                : otherClient.PatchAsJsonAsync($"/api/tickets/{otherTicket.Id}/assignment", new { assignedToUserId = other.Id }))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(held.IsCompleted);
        }
        finally
        {
            pause.Resume.TrySetResult();
            using var response = await held;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await AssertNoInvalidAssignmentAsync(user.Id);
        await AssertNoInvalidAssignmentAsync(other.Id);
    }

    [Fact]
    public async Task SharedUserLock_AllowsTwoConcurrentAssignmentsToDifferentTickets()
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var firstTicket = await CreateTicketAsync();
        var secondTicket = await CreateTicketAsync();
        var pause = new PauseRelatedSave(firstTicket.Id, null);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var firstClient = factory.CreateClient();
        using var secondClient = fixture.Factory.CreateClient();
        var first = firstClient.PatchAsJsonAsync($"/api/tickets/{firstTicket.Id}/assignment", new { assignedToUserId = user.Id });
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var second = await secondClient.PatchAsJsonAsync($"/api/tickets/{secondTicket.Id}/assignment", new { assignedToUserId = user.Id })
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.False(first.IsCompleted);
        }
        finally
        {
            pause.Resume.TrySetResult();
            using var response = await first;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        firstTicket.AssignedToUserId = secondTicket.AssignedToUserId = user.Id;
        await AssertStoredAndReturnedAsync(firstTicket);
        await AssertStoredAndReturnedAsync(secondTicket);
    }

    [Fact]
    public async Task AssignmentLock_BlocksDeletionUntilCommit_ThenForeignKeyRejectsDeletion()
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync();
        var pause = new PauseRelatedSave(ticket.Id, null);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var client = factory.CreateClient();
        var assignment = client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = user.Id });
        Task<int>? deletion = null;
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        try
        {
            var holderPid = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await db.Database.OpenConnectionAsync();
            var waiterPid = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
            deletion = db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
            await AssertBlockedByAsync(waiterPid, holderPid);
            pause.Resume.TrySetResult();
            using var response = await assignment;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var error = await Assert.ThrowsAsync<PostgresException>(() => deletion);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
            ticket.AssignedToUserId = user.Id;
            await AssertStoredAndReturnedAsync(ticket);
            await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt));
        }
        finally
        {
            pause.Resume.TrySetResult();
            (await assignment).Dispose();
            if (deletion is not null)
            {
                try { await deletion; }
                catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation) { }
            }
        }
    }

    private async Task AssertNoInvalidAssignmentAsync(int userId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Tickets.AnyAsync(t => t.AssignedToUserId == userId && t.AssignedToUser!.Role == UserRole.User));
    }

    private async Task AssertBlockedByAsync(int waiterPid, int holderPid)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        // Observar el grafo real de espera; el timeout detecta errores, no decide el orden.
        while (true)
        {
            var blocked = await db.Database.SqlQuery<int>($"SELECT CASE WHEN {holderPid} = ANY(pg_blocking_pids({waiterPid})) THEN 1 ELSE 0 END AS \"Value\"")
                .SingleAsync(timeout.Token);
            if (blocked == 1) return;
            await Task.Yield();
        }
    }

    private sealed class PauseRelatedSave(int? ticketId, int? userId) : SaveChangesInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if ((ticketId is int t && db.ChangeTracker.Entries<Ticket>().Any(e => e.Entity.Id == t)) ||
                (userId is int u && db.ChangeTracker.Entries<User>().Any(e => e.Entity.Id == u)))
            {
                Reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObserveUserLock(string clause) : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Executing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(clause, StringComparison.Ordinal))
                Executing.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
            return ValueTask.FromResult(result);
        }
    }
}
