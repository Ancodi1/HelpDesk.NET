using System.Net;
using System.Net.Http.Json;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HelpDesk.Api.Models;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Admin)]
    public async Task AssignedToMe_RejectsOtherRoles(UserRole role)
    {
        var actor = await SeedHttpUserAsync(role);
        using var client = CreateAuthenticatedClient(actor);
        foreach (var value in new[] { "true", "false" })
        {
            using var response = await client.GetAsync($"/api/tickets?assignedToMe={value}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("TRUE")]
    [InlineData("true&assignedToMe=false")]
    public async Task AssignedToMe_RejectsInvalidOrRepeatedValues(string value)
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.GetAsync($"/api/tickets?assignedToMe={value}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssignedToMe_ComposesFiltersBeforeCountsAndPagination_AndPreservesDefault()
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var own = await CreateTicketAsync(createdByUserId: actor.Id, assignedToUserId: other.Id);
        var assigned = await CreateTicketAsync(assignedToUserId: actor.Id);
        var both = await CreateTicketAsync(createdByUserId: actor.Id, assignedToUserId: actor.Id);
        await CreateTicketAsync(assignedToUserId: other.Id);
        var ids = new[] { own.Id, assigned.Id, both.Id };
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            await db.Tickets.Where(t => ids.Contains(t.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Description, title));
        }
        using var client = CreateAuthenticatedClient(actor);
        foreach (var filter in new[] { "", "&assignedToMe=false", "&assignedToMe=true" })
        {
            var expected = filter.EndsWith("true") ? new[] { both.Id, assigned.Id } : new[] { both.Id, assigned.Id, own.Id };
            var query = $"search={title}&status=Open&priority=Medium&category=Other&pageSize=1{filter}&assignedToUserId={other.Id}";
            var collected = new List<int>();
            for (var page = 1; page <= expected.Length + 1; page++)
            {
                using var response = await client.GetAsync($"/api/tickets?{query}&page={page}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var result = (await response.Content.ReadFromJsonAsync<TicketPageDto>(JsonOptions))!;
                Assert.Equal(expected.Length, result.TotalCount);
                Assert.Equal(expected.Length, result.TotalPages);
                if (page <= expected.Length) collected.Add(Assert.Single(result.Items).Id);
                else Assert.Empty(result.Items);
            }
            Assert.Equal(expected.OrderDescending(), collected);
        }
        using var empty = await client.GetAsync($"/api/tickets?search={title}&assignedToMe=true&status=Resolved");
        var emptyPage = (await empty.Content.ReadFromJsonAsync<TicketPageDto>(JsonOptions))!;
        Assert.Empty(emptyPage.Items);
        Assert.Equal(0, emptyPage.TotalCount);
    }
}
