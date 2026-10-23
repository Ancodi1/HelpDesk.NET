using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

// Misma clase/fixture y limpieza que el CRUD; el archivo agrupa solo los casos de prioridad.
public partial class TicketEndpointsTests
{
    [Theory]
    [InlineData("Low", TicketPriority.Low)]
    [InlineData("Medium", TicketPriority.Medium)]
    [InlineData("High", TicketPriority.High)]
    [InlineData("Critical", TicketPriority.Critical)]
    public async Task PostTicket_WithPriority_PersistsRequestedValue(string priority, TicketPriority expected)
    {
        using var client = await CreateAdminClientAsync();
        using var response = await client.PostAsJsonAsync("/api/tickets", new { title, description = Description, priority });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(ticket);
        Assert.True(ticket.Id > 0);
        Assert.Equal(title, ticket.Title);
        Assert.Equal(Description, ticket.Description);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(expected, ticket.Priority);
        Assert.Equal($"/api/tickets/{ticket.Id}", response.Headers.Location?.OriginalString);
        await AssertPriorityTextAsync(response, priority);

        // Volver a leer para tener la precisión de fecha de PostgreSQL al comparar con GET.
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(expected, stored.Priority);
        Assert.Equal(ticket.CreatedAt.Ticks / 10, stored.CreatedAt.Ticks / 10);
        Assert.Equal(title, stored.Title);
        Assert.Equal(Description, stored.Description);
        Assert.Equal(TicketStatus.Open, stored.Status);
        await AssertStoredAndReturnedAsync(stored);
    }

    [Theory]
    [InlineData("\"Inventado\"")]
    [InlineData("2")]
    [InlineData("\"2\"")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"low\"")]
    [InlineData("{}")]
    public async Task PostTicket_InvalidPriority_ReturnsBadRequestWithoutCreatingTicket(string priorityJson)
    {
        var payload = $"{{\"title\":{JsonSerializer.Serialize(title)},\"description\":{JsonSerializer.Serialize(Description)},\"priority\":{priorityJson}}}";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var client = await CreateAdminClientAsync();
        using var response = await client.PostAsync("/api/tickets", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertNoPostTicketAsync();
    }

    [Fact]
    public async Task PostTicket_MalformedPriorityJson_ReturnsBriefBadRequest()
    {
        using var content = new StringContent("{\"priority\":", Encoding.UTF8, "application/json");
        using var client = await CreateAdminClientAsync();
        using var response = await client.PostAsync("/api/tickets", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertBriefJsonErrorAsync(response);
        await AssertNoPostTicketAsync();
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    public async Task PatchPriority_ValidValue_PersistsOnlyPriority(TicketPriority requested)
    {
        var initial = requested == TicketPriority.Low ? TicketPriority.Critical : TicketPriority.Low;
        var original = await CreateTicketAsync(TicketStatus.Resolved, initial);
        using var client = await CreateAdminClientAsync();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/priority",
            new { priority = requested.ToString(), title = "ignored", status = "Closed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(returned);
        original.Priority = requested;
        AssertTicketEqual(original, returned);
        await AssertPriorityTextAsync(response, requested.ToString());
        await AssertStoredAndReturnedAsync(original);
    }

    [Theory]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.Medium)]
    [InlineData(TicketPriority.High)]
    [InlineData(TicketPriority.Critical)]
    public async Task PatchPriority_SameValue_ReturnsUnchangedTicket(TicketPriority priority)
    {
        var original = await CreateTicketAsync(TicketStatus.InProgress, priority);
        using var client = await CreateAdminClientAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/priority",
                new { priority = priority.ToString() });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
            Assert.NotNull(returned);
            AssertTicketEqual(original, returned);
            await AssertStoredAndReturnedAsync(original);
        }
    }

    [Theory]
    [InlineData("{\"priority\":\"Inventado\"}", false)]
    [InlineData("{\"priority\":2}", true)]
    [InlineData("{\"priority\":\"2\"}", false)]
    [InlineData("{\"priority\":null}", false)]
    [InlineData("{}", false)]
    [InlineData("{\"priority\":\"\"}", false)]
    [InlineData("{\"priority\":", true)]
    [InlineData("[]", true)]
    public async Task PatchPriority_InvalidBody_ReturnsBadRequestWithoutChanges(string payload, bool bindingError)
    {
        var original = await CreateTicketAsync(TicketStatus.Resolved, TicketPriority.High);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var client = await CreateAdminClientAsync();
        using var response = await client.PatchAsync($"/api/tickets/{original.Id}/priority", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        if (bindingError)
        {
            await AssertBriefJsonErrorAsync(response);
        }
        else
        {
            await AssertHasErrorAsync(response);
        }
        await AssertStoredAndReturnedAsync(original);
    }

    [Fact]
    public async Task PatchPriority_UnknownTicket_ReturnsNotFound()
    {
        var control = await CreateTicketAsync();
        await AssertMissingIdAsync();
        using var client = await CreateAdminClientAsync();
        using var response = await client.PatchAsJsonAsync("/api/tickets/-1/priority", new { priority = "Critical" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredAndReturnedAsync(control);
    }

    private async Task AssertNoPostTicketAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Tickets.AnyAsync(t => t.Title == title));
    }

    private static async Task AssertPriorityTextAsync(HttpResponseMessage response, string expected)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("priority").GetString());
    }

    private static async Task AssertBriefJsonErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var property = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("error", property.Name);
        Assert.Equal("El cuerpo JSON de la petición no es válido.", property.Value.GetString());
    }
}
