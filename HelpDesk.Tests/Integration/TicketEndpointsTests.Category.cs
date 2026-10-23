using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

// Misma clase/fixture y limpieza que el CRUD; el archivo agrupa los casos de categoría.
public partial class TicketEndpointsTests
{
    [Theory]
    [InlineData("Hardware", TicketCategory.Hardware)]
    [InlineData("Other", TicketCategory.Other)]
    [InlineData("Software", TicketCategory.Software)]
    [InlineData("Network", TicketCategory.Network)]
    [InlineData("Access", TicketCategory.Access)]
    public async Task PostTicket_WithCategory_PersistsRequestedValue(string category, TicketCategory expected)
    {
        using var client = await CreateAdminClientAsync();
        using var response = await client.PostAsJsonAsync("/api/tickets", new { title, description = Description, category });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(ticket);
        Assert.True(ticket.Id > 0);
        Assert.Equal(title, ticket.Title);
        Assert.Equal(Description, ticket.Description);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Equal(expected, ticket.Category);
        Assert.Equal($"/api/tickets/{ticket.Id}", response.Headers.Location?.OriginalString);
        await AssertCategoryTextAsync(response, category);

        // Volver a leer para tener la precisión de fecha de PostgreSQL al comparar con GET.
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(expected, stored.Category);
        Assert.Equal(ticket.CreatedAt.Ticks / 10, stored.CreatedAt.Ticks / 10);
        Assert.Equal(title, stored.Title);
        Assert.Equal(Description, stored.Description);
        Assert.Equal(TicketStatus.Open, stored.Status);
        Assert.Equal(TicketPriority.Medium, stored.Priority);
        await AssertStoredAndReturnedAsync(stored);
    }

    [Theory]
    [InlineData("\"Inventado\"")]
    [InlineData("2")]
    [InlineData("\"2\"")]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"hardware\"")]
    [InlineData("{}")]
    public async Task PostTicket_InvalidCategory_ReturnsBadRequestWithoutCreatingTicket(string categoryJson)
    {
        var payload = $"{{\"title\":{JsonSerializer.Serialize(title)},\"description\":{JsonSerializer.Serialize(Description)},\"category\":{categoryJson}}}";
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var client = await CreateAdminClientAsync();
        using var response = await client.PostAsync("/api/tickets", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertNoPostTicketAsync();
    }

    [Fact]
    public async Task PostTicket_MalformedCategoryJson_ReturnsBriefBadRequest()
    {
        using var content = new StringContent("{\"category\":", Encoding.UTF8, "application/json");
        using var client = await CreateAdminClientAsync();
        using var response = await client.PostAsync("/api/tickets", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertBriefJsonErrorAsync(response);
        await AssertNoPostTicketAsync();
    }

    [Theory]
    [InlineData(TicketCategory.Hardware)]
    [InlineData(TicketCategory.Other)]
    [InlineData(TicketCategory.Software)]
    [InlineData(TicketCategory.Network)]
    [InlineData(TicketCategory.Access)]
    public async Task PatchCategory_ValidValue_PersistsOnlyCategory(TicketCategory requested)
    {
        var initial = requested == TicketCategory.Hardware ? TicketCategory.Access : TicketCategory.Hardware;
        var original = await CreateTicketAsync(TicketStatus.Resolved, TicketPriority.Critical, initial);
        using var client = await CreateAdminClientAsync();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/category",
            new { category = requested.ToString(), title = "ignored", description = "ignored", status = "Closed", priority = "Low", createdAt = DateTime.UnixEpoch });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(returned);
        original.Category = requested;
        AssertTicketEqual(original, returned);
        await AssertCategoryTextAsync(response, requested.ToString());
        await AssertStoredAndReturnedAsync(original);
    }

    [Theory]
    [InlineData(TicketCategory.Hardware)]
    [InlineData(TicketCategory.Other)]
    [InlineData(TicketCategory.Software)]
    [InlineData(TicketCategory.Network)]
    [InlineData(TicketCategory.Access)]
    public async Task PatchCategory_SameValue_ReturnsUnchangedTicket(TicketCategory category)
    {
        var original = await CreateTicketAsync(TicketStatus.InProgress, TicketPriority.High, category);
        using var client = await CreateAdminClientAsync();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/category",
                new { category = category.ToString() });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
            Assert.NotNull(returned);
            AssertTicketEqual(original, returned);
            await AssertStoredAndReturnedAsync(original);
        }
    }

    [Theory]
    [InlineData("{\"category\":\"Inventado\"}", false)]
    [InlineData("{\"category\":2}", true)]
    [InlineData("{\"category\":\"2\"}", false)]
    [InlineData("{\"category\":null}", false)]
    [InlineData("{}", false)]
    [InlineData("{\"category\":\"\"}", false)]
    [InlineData("{\"category\":", true)]
    [InlineData("[]", true)]
    public async Task PatchCategory_InvalidBody_ReturnsBadRequestWithoutChanges(string payload, bool bindingError)
    {
        var original = await CreateTicketAsync(TicketStatus.Resolved, TicketPriority.High, TicketCategory.Network);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var client = await CreateAdminClientAsync();
        using var response = await client.PatchAsync($"/api/tickets/{original.Id}/category", content);

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
    public async Task PatchCategory_UnknownTicket_ReturnsNotFound()
    {
        var control = await CreateTicketAsync();
        await AssertMissingIdAsync();
        using var client = await CreateAdminClientAsync();
        using var response = await client.PatchAsJsonAsync("/api/tickets/-1/category", new { category = "Access" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredAndReturnedAsync(control);
    }

    private static async Task AssertCategoryTextAsync(HttpResponseMessage response, string expected)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("category").GetString());
    }

}
