using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    [InlineData(UserRole.User, "own", true)]
    [InlineData(UserRole.User, "other", false)]
    [InlineData(UserRole.User, "historical", false)]
    [InlineData(UserRole.User, "assigned", false)]
    [InlineData(UserRole.Technician, "own", true)]
    [InlineData(UserRole.Technician, "assigned", true)]
    [InlineData(UserRole.Technician, "other", false)]
    [InlineData(UserRole.Technician, "historical", false)]
    [InlineData(UserRole.Admin, "other", true)]
    [InlineData(UserRole.Admin, "own", true)]
    [InlineData(UserRole.Admin, "historical", true)]
    public async Task Activities_Visibility(UserRole role, string scenario, bool allowed)
    {
        var actor = await SeedHttpUserAsync(role);
        var other = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync(createdByUserId: scenario == "own" ? actor.Id : scenario == "historical" ? null : other.Id,
            assignedToUserId: scenario == "assigned" ? actor.Id : null);
        using var admin = await CreateAdminClientAsync();
        using var change = await admin.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new { priority = "High" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        using var client = CreateAuthenticatedClient(actor);
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/activities");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);
        if (allowed) Assert.Single((await read.Content.ReadFromJsonAsync<TicketActivityPageDto>())!.Items);
        else Assert.Empty(await read.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("status", "InProgress", "Open", "Status")]
    [InlineData("priority", "High", "Medium", "Priority")]
    [InlineData("category", "Network", "Other", "Category")]
    public async Task Activities_RealChangesOnlyAndHistoricalActor(string route, string value, string old, string field)
    {
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync();
        using var client = CreateAuthenticatedClient(actor);
        var before = DateTime.UtcNow;
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}", new Dictionary<string, object>
            { [route] = value, ["actorUserId"] = -1, ["oldValue"] = "forged", ["createdAt"] = DateTime.UnixEpoch });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var activity = Assert.Single(await db.TicketActivities.AsNoTracking().Where(a => a.TicketId == ticket.Id).ToListAsync());
        Assert.Equal(field, activity.Field); Assert.Equal(old, activity.OldValue); Assert.Equal(value, activity.NewValue);
        Assert.Equal(actor.Id, activity.ActorUserId); Assert.Equal(actor.Name, activity.ActorNameSnapshot);
        Assert.Equal(DateTimeKind.Utc, activity.CreatedAt.Kind); Assert.True(activity.CreatedAt >= before.AddSeconds(-1));
        await db.Users.Where(u => u.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.Name, "Renamed actor"));
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/activities");
        var item = Assert.Single((await read.Content.ReadFromJsonAsync<TicketActivityPageDto>())!.Items);
        Assert.Equal(actor.Name, item.ActorName);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "actorName", "actorUserId", "createdAt", "field", "id", "newAssigneeName", "newValue", "oldAssigneeName", "oldValue", "ticketId" },
            json.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Activities_AssignmentSnapshotsAndUnassignment()
    {
        var old = await SeedHttpUserAsync(UserRole.Technician);
        var next = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync(assignedToUserId: old.Id);
        using var client = await CreateAdminClientAsync();
        foreach (var id in new int?[] { next.Id, next.Id, null, null })
        {
            using var write = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = id });
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await db.Users.Where(u => u.Id == old.Id || u.Id == next.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Name, "Renamed"));
        var items = await db.TicketActivities.Where(a => a.TicketId == ticket.Id).OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(old.Id.ToString(), items[0].OldValue); Assert.Equal(next.Id.ToString(), items[0].NewValue);
        Assert.Equal(old.Name, items[0].OldAssigneeNameSnapshot); Assert.Equal(next.Name, items[0].NewAssigneeNameSnapshot);
        Assert.Equal(next.Name, items[1].OldAssigneeNameSnapshot); Assert.Null(items[1].NewAssigneeNameSnapshot);
        Assert.Null(items[1].NewValue);
        // Historical assignees are values/snapshots, not directory relationships.
        Assert.Equal(2, await db.Users.Where(u => u.Id == old.Id || u.Id == next.Id).ExecuteDeleteAsync());
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/activities");
        var page = (await read.Content.ReadFromJsonAsync<TicketActivityPageDto>())!;
        Assert.Equal(next.Name, page.Items[0].OldAssigneeName);
        Assert.Equal(old.Name, page.Items[1].OldAssigneeName);
    }

    [Theory]
    [InlineData("status", "Closed", 409)]
    [InlineData("status", "invalid", 400)]
    [InlineData("priority", "invalid", 400)]
    [InlineData("category", "invalid", 400)]
    [InlineData("assignment", "invalid", 400)]
    public async Task Activities_RejectedOperationsHaveNoEvents(string route, string value, int status)
    {
        var ticket = await CreateTicketAsync();
        using var client = await CreateAdminClientAsync();
        using var response = route == "assignment"
            ? await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}", new { assignedToUserId = -1 })
            : await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}", new Dictionary<string, string> { [route] = value });
        Assert.Equal(status, (int)response.StatusCode);
        await AssertStoredAndReturnedAsync(ticket);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().TicketActivities.AnyAsync(a => a.TicketId == ticket.Id));
    }

    [Theory]
    [InlineData(UserRole.User, "status")]
    [InlineData(UserRole.Technician, "priority")]
    [InlineData(UserRole.Technician, "category")]
    [InlineData(UserRole.Technician, "assignment")]
    public async Task Activities_ForbiddenWritesHaveNoEvents(UserRole role, string route)
    {
        var actor = await SeedHttpUserAsync(role);
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id, assignedToUserId: role == UserRole.Technician ? actor.Id : null);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}", new { status = "InProgress", priority = "High", category = "Network", assignedToUserId = (int?)null });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().TicketActivities.AnyAsync(a => a.TicketId == ticket.Id));
    }

    [Theory]
    [InlineData("pageSize=")][InlineData("pageSize=0")][InlineData("pageSize=101")]
    [InlineData("pageSize=1&pageSize=2")][InlineData("beforeId=0")][InlineData("beforeId=-1")]
    [InlineData("beforeId=2147483648")][InlineData("beforeId=1&beforeId=2")]
    public async Task Activities_InvalidCursorRejected(string query)
    {
        var ticket = await CreateTicketAsync();
        using var client = await CreateAdminClientAsync();
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/activities?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
    }

    [Fact]
    public async Task Activities_CursorStableWithNewChangesAndOtherTickets()
    {
        var ticket = await CreateTicketAsync();
        var other = await CreateTicketAsync();
        using var client = await CreateAdminClientAsync();
        for (var i = 0; i < 21; i++)
        {
            using var change = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new { priority = i % 2 == 0 ? "High" : "Low" });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        }
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/activities");
        var first = (await read.Content.ReadFromJsonAsync<TicketActivityPageDto>())!;
        Assert.Equal(20, first.Items.Count); Assert.True(first.HasMore); Assert.Equal(first.Items[^1].Id, first.NextBeforeId);
        using var inserted = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new { priority = "Critical" });
        using var foreign = await client.PatchAsJsonAsync($"/api/tickets/{other.Id}/priority", new { priority = "High" });
        using var readNext = await client.GetAsync($"/api/tickets/{ticket.Id}/activities?beforeId={first.NextBeforeId}");
        var next = (await readNext.Content.ReadFromJsonAsync<TicketActivityPageDto>())!;
        var item = Assert.Single(next.Items); Assert.True(item.Id < first.NextBeforeId);
        Assert.False(next.HasMore); Assert.Null(next.NextBeforeId); Assert.Equal(ticket.Id, item.TicketId);
        Assert.Equal(first.Items.OrderByDescending(a => a.Id).Select(a => a.Id), first.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task Activities_ProtectTicketAndActorWithForeignKeysAndExposeNoWriteRoutes()
    {
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync();
        using var client = CreateAuthenticatedClient(actor);
        using var change = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new { priority = "High" });
        using var deletion = await client.DeleteAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        using var json = JsonDocument.Parse(await deletion.Content.ReadAsStringAsync());
        Assert.Equal("ticket_has_activities", json.RootElement.GetProperty("code").GetString());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Tickets.Where(t => t.Id == ticket.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        error = await Assert.ThrowsAsync<PostgresException>(() => db.Users.Where(u => u.Id == actor.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        using var forged = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/activities", new { field = "Priority", newValue = "Critical" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, forged.StatusCode);
        using var edit = await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}/activities", new { field = "Priority" });
        using var erase = await client.DeleteAsync($"/api/tickets/{ticket.Id}/activities");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edit.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, erase.StatusCode);
        using var missing = await client.GetAsync("/api/tickets/-1/activities");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Single(await db.TicketActivities.Where(a => a.TicketId == ticket.Id).ToListAsync());
    }

    [Theory]
    [InlineData("status")][InlineData("priority")][InlineData("category")][InlineData("assignment")]
    public async Task Activities_FailureAfterSaveRollsBackBothRows(string route)
    {
        var ticket = await CreateTicketAsync();
        var assignee = await SeedHttpUserAsync(UserRole.Technician);
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddDbContext<HelpDeskDbContext>(o => o.AddInterceptors(new FailActivitySave(ticket.Id)))));
        using var client = await CreateAdminClientAsync(factory);
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{route}",
            new { status = "InProgress", priority = "High", category = "Network", assignedToUserId = assignee.Id });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertStoredAndReturnedAsync(ticket);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().TicketActivities.AnyAsync(a => a.TicketId == ticket.Id));
    }

    [Fact]
    public async Task Activities_TechnicianIsActorAndCreationAndTextEditsHaveNoEvents()
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: actor.Id);
        using var admin = await CreateAdminClientAsync();
        using var initial = await admin.GetAsync($"/api/tickets/{ticket.Id}/activities?pageSize=100");
        var empty = (await initial.Content.ReadFromJsonAsync<TicketActivityPageDto>())!;
        Assert.Empty(empty.Items); Assert.False(empty.HasMore); Assert.Null(empty.NextBeforeId);
        using var text = await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}", new { title = ticket.Title, description = "Updated text" });
        Assert.Equal(HttpStatusCode.OK, text.StatusCode);
        using var tech = CreateAuthenticatedClient(actor);
        using var change = await tech.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var activity = await db.TicketActivities.SingleAsync(a => a.TicketId == ticket.Id);
        Assert.Equal(actor.Id, activity.ActorUserId); Assert.Equal(actor.Name, activity.ActorNameSnapshot);
        Assert.Equal("Status", activity.Field);
    }

    [Theory]
    [InlineData("field")] [InlineData("unchanged")] [InlineData("value")] [InlineData("names")]
    [InlineData("ticket")] [InlineData("actor")]
    public async Task Activities_DatabaseRejectsInvalidRows(string invalid)
    {
        var ticket = await CreateTicketAsync();
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.TicketActivities.Add(new TicketActivity
        {
            TicketId = invalid == "ticket" ? -1 : ticket.Id,
            ActorUserId = invalid == "actor" ? -1 : actor.Id,
            ActorNameSnapshot = actor.Name, Field = invalid == "field" ? "Title" : "Priority",
            OldValue = "Medium", NewValue = invalid == "unchanged" ? "Medium" : invalid == "value" ? "Unknown" : "High",
            OldAssigneeNameSnapshot = invalid == "names" ? "Invalid name" : null, CreatedAt = DateTime.UtcNow
        });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(invalid is "ticket" or "actor" ? PostgresErrorCodes.ForeignKeyViolation : PostgresErrorCodes.CheckViolation, postgres.SqlState);
        db.ChangeTracker.Clear();
        Assert.False(await db.TicketActivities.AnyAsync(a => a.TicketId == ticket.Id));
    }

    private sealed class FailActivitySave(int ticketId) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TicketActivity>().Any(e => e.Entity.TicketId == ticketId))
                throw new InvalidOperationException("Simulated failure before commit");
            return ValueTask.FromResult(result);
        }
    }
}
