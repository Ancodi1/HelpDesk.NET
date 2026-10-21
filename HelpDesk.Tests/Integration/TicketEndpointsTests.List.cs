using System.Net;
using System.Net.Http.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    private async Task<List<Ticket>> SeedListAsync(int count = 13)
    {
        var tickets = Enumerable.Range(0, count).Select(i => new Ticket
        {
            Title = $"{title}-Título-{i}", Description = $"Descripción {title} detalle-{i}",
            CreatedAt = DateTime.UnixEpoch.AddDays(i / 3),
            Status = (TicketStatus)(i % 4), Priority = (TicketPriority)(i % 4),
            Category = (TicketCategory)(i % 5)
        }).ToList();
        foreach (var ticket in tickets) ownedTitles.Add(ticket.Title);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Tickets.AddRange(tickets);
        await db.SaveChangesAsync();
        foreach (var ticket in tickets) ownedTicketIds.Add(ticket.Id);
        return tickets;
    }

    private async Task<TicketPageDto> GetPageAsync(string query)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var page = await response.Content.ReadFromJsonAsync<TicketPageDto>(JsonOptions);
        Assert.NotNull(page);
        return page;
    }

    [Fact]
    public async Task List_DefaultPagination_UsesNewEnvelope()
    {
        await SeedListAsync();
        var page = await GetPageAsync("");
        Assert.Equal(1, page.Page);
        Assert.Equal(10, page.PageSize);
        Assert.Equal(Math.Min(10, page.TotalCount), page.Items.Count);
        Assert.Equal((int)Math.Ceiling(page.TotalCount / 10d), page.TotalPages);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var expected = await db.Tickets.AsNoTracking().OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id).Take(10).ToListAsync();
        Assert.Equal(await db.Tickets.CountAsync(), page.TotalCount);
        Assert.Equal(expected.Select(t => t.Id), page.Items.Select(t => t.Id));
        for (var i = 0; i < expected.Count; i++) AssertTicketEqual(expected[i], page.Items[i]);
    }

    [Theory]
    [InlineData("status=Open")]
    [InlineData("status=InProgress")]
    [InlineData("status=Resolved")]
    [InlineData("status=Closed")]
    [InlineData("priority=Low")]
    [InlineData("priority=Medium")]
    [InlineData("priority=High")]
    [InlineData("priority=Critical")]
    [InlineData("category=Hardware")]
    [InlineData("category=Software")]
    [InlineData("category=Network")]
    [InlineData("category=Access")]
    [InlineData("category=Other")]
    [InlineData("status=Open&priority=Low&category=Hardware")]
    public async Task List_Filters_AreCombinedInDatabase(string filter)
    {
        var tickets = await SeedListAsync();
        IEnumerable<Ticket> expected = tickets;
        foreach (var pair in filter.Split('&'))
        {
            var parts = pair.Split('=');
            expected = parts[0] switch
            {
                "status" => expected.Where(t => t.Status.ToString() == parts[1]),
                "priority" => expected.Where(t => t.Priority.ToString() == parts[1]),
                _ => expected.Where(t => t.Category.ToString() == parts[1])
            };
        }
        var ordered = expected.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToList();
        var page = await GetPageAsync($"search={title}&{filter}&pageSize=100");
        Assert.Equal(ordered.Count, page.TotalCount);
        Assert.Equal(ordered.Count == 0 ? 0 : 1, page.TotalPages);
        Assert.Equal(ordered.Select(t => t.Id), page.Items.Select(t => t.Id));
        for (var i = 0; i < ordered.Count; i++) AssertTicketEqual(ordered[i], page.Items[i]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_Search_MatchesTitleAndDescriptionIgnoringCase(bool inTitle)
    {
        var tickets = await SeedListAsync();
        var search = inTitle ? $"{title.ToUpperInvariant()}-TÍTULO-7" : $"{title.ToUpperInvariant()} dEtAlLe-7";
        var page = await GetPageAsync($"search={Uri.EscapeDataString(search)}&category=Network&priority=Critical&status=Closed");
        // detalle/título 7 es Network, Critical y Closed; GUID en el texto evita coincidencias ajenas.
        Assert.Equal(tickets[7].Id, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalCount);
        Assert.All(page.Items, t => Assert.True(t.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                                   t.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task List_SearchLiteralWildcardsAndSqlText_DoesNotBroadenResults()
    {
        await SeedListAsync();
        foreach (var search in new[] { $"{title}%_\\", $"{title}' OR 1=1 --" })
        {
            var page = await GetPageAsync($"search={Uri.EscapeDataString(search)}");
            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
            Assert.Equal(0, page.TotalPages);
        }
    }

    [Fact]
    public async Task List_StableOrderingAndConsecutivePages_PreserveTotals()
    {
        var tickets = await SeedListAsync();
        var first = await GetPageAsync($"search={title}&pageSize=5");
        var second = await GetPageAsync($"search={title}&pageSize=5&page=2");
        var third = await GetPageAsync($"search={title}&pageSize=5&page=3");
        var repeated = await GetPageAsync($"search={title}&pageSize=5&page=2");
        var combined = first.Items.Concat(second.Items).Concat(third.Items).ToList();
        Assert.Equal(tickets.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Select(t => t.Id), combined.Select(t => t.Id));
        Assert.Equal(13, combined.Select(t => t.Id).Distinct().Count());
        Assert.Equal(second.Items.Select(t => t.Id), repeated.Items.Select(t => t.Id));
        foreach (var page in new[] { first, second, third })
        {
            Assert.Equal(13, page.TotalCount);
            Assert.Equal(3, page.TotalPages);
        }
        Assert.Equal(2, second.Page);
        var outside = await GetPageAsync($"search={title}&page=2147483647&pageSize=1");
        Assert.Empty(outside.Items);
        Assert.Equal(13, outside.TotalCount);
        Assert.Equal(13, outside.TotalPages);
    }

    [Fact]
    public async Task List_MaximumPageSize_IsAccepted()
    {
        await SeedListAsync(101);
        var page = await GetPageAsync($"search={title}&pageSize=100");
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(100, page.PageSize);
        Assert.Equal(101, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [Theory]
    [InlineData("status=invalid")]
    [InlineData("status=0")]
    [InlineData("status=open")]
    [InlineData("status=")]
    [InlineData("priority=invalid")]
    [InlineData("priority=1")]
    [InlineData("priority=high")]
    [InlineData("category=invalid")]
    [InlineData("category=2")]
    [InlineData("category=hardware")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=abc")]
    [InlineData("page=1.5")]
    [InlineData("page=")]
    [InlineData("page=2147483648")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=abc")]
    [InlineData("pageSize=")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=2147483648")]
    [InlineData("page=1&page=2")]
    [InlineData("status=Open&status=Closed")]
    [InlineData("search=a&search=b")]
    public async Task List_InvalidParameter_ReturnsExplanatoryJson(string query)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        await AssertHasErrorAsync(response);
    }
}
