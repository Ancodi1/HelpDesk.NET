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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comments_ReassignmentFirstRevokesReadAndPublication(bool write)
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: actor.Id);
        var pause = new PauseTicketAfterSave(ticket.Id);
        var probe = new LockCommandProbe(write ? "FOR UPDATE" : "FOR SHARE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var admin = await CreateAdminClientAsync(firstFactory);
        using var tech = CreateAuthenticatedClient(actor, secondFactory);
        var first = admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = other.Id });
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = write ? tech.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "Must not persist" })
                : tech.GetAsync($"/api/tickets/{ticket.Id}/comments");
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder);
            Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var a = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var b = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(HttpStatusCode.OK, a.StatusCode); Assert.Equal(HttpStatusCode.NotFound, b.StatusCode);
            Assert.Empty(await b.Content.ReadAsStringAsync());
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().TicketComments.AnyAsync(c => c.TicketId == ticket.Id));
        }
        finally
        {
            pause.Resume.TrySetResult(); (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comments_ReadOrPublicationFirstCompletesBeforeReassignment(bool write)
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: actor.Id);
        var pause = new PauseCommentReadOrSave(ticket.Id);
        var probe = new LockCommandProbe("UPDATE \"Tickets\"");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var tech = CreateAuthenticatedClient(actor, firstFactory);
        using var admin = await CreateAdminClientAsync(secondFactory);
        var first = write ? tech.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "Allowed before reassignment" })
            : tech.GetAsync($"/api/tickets/{ticket.Id}/comments");
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = other.Id });
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder); Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var a = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var b = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(write ? HttpStatusCode.Created : HttpStatusCode.OK, a.StatusCode);
            Assert.Equal(HttpStatusCode.OK, b.StatusCode);
            using var denied = await tech.GetAsync($"/api/tickets/{ticket.Id}/comments");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            Assert.Equal(write ? 1 : 0, await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().TicketComments.CountAsync(c => c.TicketId == ticket.Id));
        }
        finally
        {
            pause.Resume.TrySetResult(); (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comments_DeleteAndPublicationSerializeInBothOrders(bool commentFirst)
    {
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync();
        var pause = new PauseCommentReadOrSave(ticket.Id);
        var probe = new LockCommandProbe("FOR UPDATE");
        using var firstFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(pause))));
        using var secondFactory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(probe))));
        using var firstClient = CreateAuthenticatedClient(actor, firstFactory);
        using var secondClient = CreateAuthenticatedClient(actor, secondFactory);
        Task<HttpResponseMessage> Post(HttpClient c) => c.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "Protected comment" });
        var first = commentFirst ? Post(firstClient) : firstClient.DeleteAsync($"/api/tickets/{ticket.Id}");
        Task<HttpResponseMessage>? second = null;
        try
        {
            var holder = await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = commentFirst ? secondClient.DeleteAsync($"/api/tickets/{ticket.Id}") : Post(secondClient);
            var waiter = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertBlockedByAsync(waiter, holder); Assert.False(second.IsCompleted);
            pause.Resume.TrySetResult();
            using var a = await first.WaitAsync(TimeSpan.FromSeconds(15));
            using var b = await second.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(commentFirst ? HttpStatusCode.Created : HttpStatusCode.NoContent, a.StatusCode);
            Assert.Equal(commentFirst ? HttpStatusCode.Conflict : HttpStatusCode.NotFound, b.StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(commentFirst, await db.Tickets.AnyAsync(t => t.Id == ticket.Id));
            Assert.Equal(commentFirst ? 1 : 0, await db.TicketComments.CountAsync(c => c.TicketId == ticket.Id));
        }
        finally
        {
            pause.Resume.TrySetResult(); (await first.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
            if (second is not null) (await second.WaitAsync(TimeSpan.FromSeconds(25))).Dispose();
        }
    }

    // Pause only after the ticket lock is held. No sleeps determine ordering.
    private sealed class PauseCommentReadOrSave(int ticketId) : DbCommandInterceptor
    {
        public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // GET page, POST insertion or DELETE statement; all occur after taking the ticket lock.
            if (command.CommandText.Contains("INSERT INTO \"TicketComments\"", StringComparison.Ordinal) ||
                command.CommandText.Contains("DELETE FROM \"Tickets\"", StringComparison.Ordinal) ||
                (command.CommandText.Contains("FROM \"TicketComments\"", StringComparison.Ordinal) &&
                 !command.CommandText.Contains("EXISTS", StringComparison.Ordinal) &&
                 command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, ticketId))))
            {
                Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
