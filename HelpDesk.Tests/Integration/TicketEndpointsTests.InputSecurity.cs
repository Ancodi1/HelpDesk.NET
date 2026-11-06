using System.Net;
using System.Net.Http.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    [Fact]
    public async Task InputSecurity_DescriptionBoundaryPersistsAndInvalidUpdatesPreserveData()
    {
        using var client = await CreateAdminClientAsync();
        var description = string.Concat(Enumerable.Repeat("😀", 5000));
        using var created = await client.PostAsJsonAsync("/api/tickets", new { title, description });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ticket = (await created.Content.ReadFromJsonAsync<Ticket>(JsonOptions))!;
        ownedTicketIds.Add(ticket.Id);
        foreach (var invalid in new[] { description + "x", "a\0b" })
        {
            using var rejected = await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}", new { title, description = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(description, stored.Description);
        using var search = await client.GetAsync("/api/tickets?search=" + new string('x', 200));
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
    }

    [Fact]
    public async Task InputSecurity_NullUserNameReturns400WithoutWriting()
    {
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        ownedUserEmails.Add(email);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new
        { name = "Name\0Null", email, password = NewTestPassword() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Users.AnyAsync(u => u.Email == email));
    }
}
