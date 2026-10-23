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
    [Fact]
    public async Task SharedUserLock_AllowsTwoConcurrentAssignmentsToDifferentTickets()
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var firstTicket = await CreateTicketAsync();
        var secondTicket = await CreateTicketAsync();
        var pause = new PauseRelatedSave(firstTicket.Id);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var firstClient = await CreateAdminClientAsync(factory);
        using var secondClient = await CreateAdminClientAsync();
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
        var pause = new PauseRelatedSave(ticket.Id);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var client = await CreateAdminClientAsync(factory);
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

    private sealed class PauseRelatedSave(int ticketId) : SaveChangesInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            if (db.ChangeTracker.Entries<Ticket>().Any(e => e.Entity.Id == ticketId))
            {
                Reached.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
