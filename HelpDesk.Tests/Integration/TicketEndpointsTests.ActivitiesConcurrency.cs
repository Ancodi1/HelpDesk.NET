using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
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
    [InlineData("priority", false)] [InlineData("priority", true)]
    [InlineData("category", false)] [InlineData("category", true)]
    [InlineData("status", false)] [InlineData("status", true)]
    [InlineData("assignment", false)] [InlineData("assignment", true)]
    public async Task Activities_ConcurrentChangesUseCommittedOldValue(string route, bool repeat)
    {
        var ticket = await CreateTicketAsync();
        var one = await SeedHttpUserAsync(UserRole.Technician);
        var two = await SeedHttpUserAsync(UserRole.Technician);
        var values = route switch
        {
            "priority" => new[] { "Medium", "High", "Low" },
            "category" => new[] { "Other", "Network", "Software" },
            "status" => new[] { "Open", "InProgress", "Resolved" },
            _ => new[] { null, one.Id.ToString(), two.Id.ToString() }
        };
        object Body(int index) => route == "assignment"
            ? new { assignedToUserId = int.Parse(values[index]!) }
            : new Dictionary<string, string?> { [route] = values[index] };
        var pause = new PauseTicketAfterSave(ticket.Id);
        var probe = new LockCommandProbe("FOR UPDATE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var firstClient = await CreateAdminClientAsync(firstFactory);
        using var secondClient = await CreateAdminClientAsync(secondFactory);
        var first = firstClient.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}", Body(1));
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = secondClient.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}", Body(repeat ? 1 : 2));
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var a = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var b = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.OK, a.StatusCode); Assert.Equal(HttpStatusCode.OK, b.StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var activities = await db.TicketActivities.Where(a => a.TicketId == ticket.Id).OrderBy(a => a.Id).ToListAsync();
            Assert.Equal(repeat ? 1 : 2, activities.Count);
            Assert.Equal(values[0], activities[0].OldValue); Assert.Equal(values[1], activities[0].NewValue);
            if (!repeat)
            {
                Assert.Equal(values[1], activities[1].OldValue); Assert.Equal(values[2], activities[1].NewValue);
                if (route == "assignment")
                {
                    Assert.Equal(one.Name, activities[1].OldAssigneeNameSnapshot);
                    Assert.Equal(two.Name, activities[1].NewAssigneeNameSnapshot);
                }
            }
        }
        finally
        {
            pause.Resume.TrySetResult(); (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    [Theory]
    [InlineData("priority", false)] [InlineData("priority", true)]
    [InlineData("category", false)] [InlineData("category", true)]
    [InlineData("status", false)] [InlineData("status", true)]
    [InlineData("assignment", false)] [InlineData("assignment", true)]
    public async Task Activities_ChangeAndDeleteSerializeInBothOrders(string route, bool changeFirst)
    {
        var ticket = await CreateTicketAsync();
        var technician = await SeedHttpUserAsync(UserRole.Technician);
        var pause = new PauseActivityOrDelete();
        var probe = new LockCommandProbe("FOR UPDATE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var firstClient = await CreateAdminClientAsync(firstFactory);
        using var secondClient = await CreateAdminClientAsync(secondFactory);
        Task<HttpResponseMessage> Change(HttpClient client) => client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}",
            new { status = "InProgress", priority = "High", category = "Network", assignedToUserId = technician.Id });
        var first = changeFirst ? Change(firstClient) : firstClient.DeleteAsync($"/api/tickets/{ticket.Id}");
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = changeFirst ? secondClient.DeleteAsync($"/api/tickets/{ticket.Id}") : Change(secondClient);
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder); Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var a = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var b = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(changeFirst ? HttpStatusCode.OK : HttpStatusCode.NoContent, a.StatusCode);
            Assert.Equal(changeFirst ? HttpStatusCode.Conflict : HttpStatusCode.NotFound, b.StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(changeFirst, await db.Tickets.AnyAsync(t => t.Id == ticket.Id));
            Assert.Equal(changeFirst ? 1 : 0, await db.TicketActivities.CountAsync(a => a.TicketId == ticket.Id));
        }
        finally
        {
            pause.Resume.TrySetResult(); (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    [Fact]
    public async Task Activities_ReadWaitsForReassignmentAndRejectsFormerTechnician()
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        var next = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: actor.Id);
        var pause = new PauseTicketAfterSave(ticket.Id);
        var probe = new LockCommandProbe("FOR SHARE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var admin = await CreateAdminClientAsync(firstFactory);
        using var tech = CreateAuthenticatedClient(actor, secondFactory);
        var first = admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = next.Id });
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = tech.GetAsync($"/api/tickets/{ticket.Id}/activities");
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            pause.Resume.TrySetResult();
            using var a = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var b = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.OK, a.StatusCode); Assert.Equal(HttpStatusCode.NotFound, b.StatusCode);
            Assert.Empty(await b.Content.ReadAsStringAsync());
        }
        finally
        {
            pause.Resume.TrySetResult(); (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Activities_AssignmentLocksHistoricalUserBeforeTicket(bool unassign)
    {
        var old = await SeedHttpUserAsync(UserRole.Technician);
        var next = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: old.Id);
        var probe = new LockCommandProbe("FOR SHARE");
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var client = await CreateAdminClientAsync(factory);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"Id\" = {old.Id} FOR NO KEY UPDATE").SingleAsync();
        var holder = ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;
        var change = client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = unassign ? (int?)null : next.Id });
        try
        {
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            // If assignment took the ticket before the old user, NOWAIT would fail here.
            await using (var ticketScope = fixture.Factory.Services.CreateAsyncScope())
            {
                var ticketDb = ticketScope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
                await using var ticketTransaction = await ticketDb.Database.BeginTransactionAsync();
                await ticketDb.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {ticket.Id} FOR UPDATE NOWAIT").SingleAsync();
                await ticketTransaction.CommitAsync();
            }
            user.Name = "Historical name at commit";
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            using var response = await change.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var activity = await db.TicketActivities.SingleAsync(a => a.TicketId == ticket.Id);
            Assert.Equal("Historical name at commit", activity.OldAssigneeNameSnapshot);
            Assert.Equal(unassign ? null : next.Name, activity.NewAssigneeNameSnapshot);
        }
        finally
        {
            await transaction.DisposeAsync();
            (await change.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    // The statement is reached only after the endpoint holds the ticket lock.
    private sealed class PauseActivityOrDelete : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"TicketActivities\"", StringComparison.Ordinal) ||
                command.CommandText.Contains("DELETE FROM \"Tickets\"", StringComparison.Ordinal))
            {
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
