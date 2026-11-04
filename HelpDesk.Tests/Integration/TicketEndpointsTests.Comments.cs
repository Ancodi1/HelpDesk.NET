using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    [Theory]
    [InlineData(UserRole.User, "own", true)]
    [InlineData(UserRole.User, "other", false)]
    [InlineData(UserRole.User, "historical", false)]
    [InlineData(UserRole.Technician, "own", true)]
    [InlineData(UserRole.Technician, "assigned", true)]
    [InlineData(UserRole.Technician, "historical", false)]
    [InlineData(UserRole.Technician, "other", false)]
    [InlineData(UserRole.Admin, "historical", true)]
    public async Task Comments_VisibilityControlsReadAndWriteIncludingClosedTickets(UserRole role, string scenario, bool allowed)
    {
        var actor = await SeedHttpUserAsync(role);
        var other = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync(TicketStatus.Closed,
            createdByUserId: scenario == "own" ? actor.Id : scenario == "historical" ? null : other.Id,
            assignedToUserId: scenario == "assigned" ? actor.Id : null);
        using var client = CreateAuthenticatedClient(actor);
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/comments");
        using var write = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "Plain comment" });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.NotFound, write.StatusCode);
        if (!allowed)
        {
            Assert.Empty(await read.Content.ReadAsStringAsync());
            Assert.Empty(await write.Content.ReadAsStringAsync());
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.Equal(allowed ? 1 : 0, await db.TicketComments.CountAsync(c => c.TicketId == ticket.Id));
        AssertTicketEqual(ticket, await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t")]
    [InlineData("\u0085\u2003\u3000")]
    [InlineData("text\0text")]
    [InlineData("long")]
    [InlineData("emoji")]
    public async Task Comments_InvalidContentDoesNotPersist(string? content)
    {
        var actor = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id);
        content = content switch { "long" => new string('x', 4001), "emoji" => string.Concat(Enumerable.Repeat("😀", 2001)), _ => content };
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().TicketComments.AnyAsync(c => c.TicketId == ticket.Id));
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("emoji")]
    public async Task Comments_BoundaryAndHistoricalAuthorPersistWithoutIncidentalFields(string kind)
    {
        var actor = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id);
        var content = kind == "ascii" ? new string('x', 4000) : string.Concat(Enumerable.Repeat("😀", 2000));
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new
        {
            content, authorUserId = -1, authorName = "Forged author", createdAt = DateTime.UnixEpoch, ticketId = -1
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "authorName", "authorUserId", "content", "createdAt", "id", "ticketId" }, json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        var comment = (await response.Content.ReadFromJsonAsync<TicketCommentDto>())!;
        Assert.Equal(actor.Id, comment.AuthorUserId); Assert.Equal(actor.Name, comment.AuthorName);
        Assert.Equal(ticket.Id, comment.TicketId); Assert.Equal(content, comment.Content);
        Assert.Equal(DateTimeKind.Utc, comment.CreatedAt.Kind); Assert.True(comment.CreatedAt > DateTime.UnixEpoch);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            await db.Users.Where(u => u.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.Name, "Renamed"));
            var stored = await db.TicketComments.AsNoTracking().SingleAsync(c => c.Id == comment.Id);
            Assert.Equal(actor.Name, stored.AuthorNameSnapshot); Assert.Equal(content, stored.Content);
            Assert.Equal(comment.CreatedAt.Ticks / 10, stored.CreatedAt.Ticks / 10);
        }
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/comments");
        var page = (await read.Content.ReadFromJsonAsync<TicketCommentPageDto>())!;
        Assert.Equal(actor.Name, Assert.Single(page.Items).AuthorName);
    }

    [Theory]
    [InlineData("pageSize=")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=1&pageSize=2")]
    [InlineData("beforeId=")]
    [InlineData("beforeId=0")]
    [InlineData("beforeId=-1")]
    [InlineData("beforeId=2147483648")]
    [InlineData("beforeId=1&beforeId=2")]
    public async Task Comments_InvalidPaginationIsBadRequest(string query)
    {
        var actor = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.GetAsync($"/api/tickets/{ticket.Id}/comments?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Comments_CursorHasNoDuplicatesAfterNewInsertAndNeverCrossesTickets()
    {
        var actor = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id);
        var other = await CreateTicketAsync(createdByUserId: actor.Id);
        using var client = CreateAuthenticatedClient(actor);
        var ids = new List<int>();
        for (var i = 0; i < 21; i++)
        {
            using var write = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = $"Comment {i}" });
            ids.Add((await write.Content.ReadFromJsonAsync<TicketCommentDto>())!.Id);
        }
        using var foreign = await client.PostAsJsonAsync($"/api/tickets/{other.Id}/comments", new { content = "Other ticket" });
        using var read = await client.GetAsync($"/api/tickets/{ticket.Id}/comments");
        var first = (await read.Content.ReadFromJsonAsync<TicketCommentPageDto>())!;
        Assert.Equal(20, first.PageSize); Assert.Equal(ids.Skip(1).Reverse(), first.Items.Select(c => c.Id));
        Assert.True(first.HasMore); Assert.Equal(first.Items[^1].Id, first.NextBeforeId);
        using var added = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "New arrival" });
        using var next = await client.GetAsync($"/api/tickets/{ticket.Id}/comments?beforeId={first.NextBeforeId}");
        var second = (await next.Content.ReadFromJsonAsync<TicketCommentPageDto>())!;
        Assert.Equal(ids[0], Assert.Single(second.Items).Id); Assert.False(second.HasMore); Assert.Null(second.NextBeforeId);
        using var empty = await client.GetAsync($"/api/tickets/{ticket.Id}/comments?beforeId={ids[0]}&pageSize=100");
        var last = (await empty.Content.ReadFromJsonAsync<TicketCommentPageDto>())!;
        Assert.Empty(last.Items); Assert.False(last.HasMore); Assert.Null(last.NextBeforeId);
    }

    [Fact]
    public async Task Comments_AbsentTicketHasSameNotFoundForGetAndPost()
    {
        using var client = await CreateAdminClientAsync();
        using var read = await client.GetAsync("/api/tickets/-1/comments");
        using var write = await client.PostAsJsonAsync("/api/tickets/-1/comments", new { content = "valid" });
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode); Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Empty(await read.Content.ReadAsStringAsync()); Assert.Empty(await write.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Comments_BlockApiAndDatabaseDeletionWithoutCascading()
    {
        var actor = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync();
        using var client = CreateAuthenticatedClient(actor);
        using var write = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "Keep this" });
        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        using var deletion = await client.DeleteAsync($"/api/tickets/{ticket.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        using var json = JsonDocument.Parse(await deletion.Content.ReadAsStringAsync());
        Assert.Equal("ticket_has_comments", json.RootElement.GetProperty("code").GetString());
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Tickets.Where(t => t.Id == ticket.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.Single(await db.TicketComments.Where(c => c.TicketId == ticket.Id).ToListAsync());
        AssertTicketEqual(ticket, await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id));
    }

    [Theory]
    [InlineData("blank")]
    [InlineData("emoji")]
    public async Task Comments_DatabaseEnforcesUnicodeWhitespaceAndUtf16(string kind)
    {
        var actor = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.TicketComments.Add(new TicketComment { TicketId = ticket.Id, AuthorUserId = actor.Id,
            AuthorNameSnapshot = actor.Name, CreatedAt = DateTime.UtcNow,
            Content = kind == "blank" ? "\u0085\u2003\u3000" : string.Concat(Enumerable.Repeat("😀", 2001)) });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
