using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

[Trait("Category", "PostgreSQL")]
public partial class TicketEndpointsTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly string title = $"integration-{Guid.NewGuid():N}";
    private readonly HashSet<int> ownedTicketIds = [];
    private readonly HashSet<string> ownedTitles = [];
    private const string Description = "Ticket exclusivo de prueba de integración.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task PostTicket_ReturnsCreatedAndPersistsTicket()
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/tickets", new { title, description = Description });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(ticket);
        Assert.True(ticket.Id > 0);
        Assert.Equal(title, ticket.Title);
        Assert.Equal(Description, ticket.Description);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Equal(DateTimeKind.Utc, ticket.CreatedAt.Kind);
        Assert.NotEqual(default, ticket.CreatedAt);
        Assert.Equal($"/api/tickets/{ticket.Id}", response.Headers.Location?.OriginalString);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(title, stored.Title);
        Assert.Equal(Description, stored.Description);
        Assert.Equal(TicketStatus.Open, stored.Status);
        Assert.Equal(TicketPriority.Medium, stored.Priority);
        // PostgreSQL conserva microsegundos, frente a los ticks de 100 ns de .NET.
        Assert.Equal(ticket.CreatedAt.Ticks / 10, stored.CreatedAt.Ticks / 10);
    }

    [Fact]
    public async Task GetTicket_ReturnsPersistedTicket()
    {
        var ticket = new Ticket { Title = title, Description = Description, CreatedAt = DateTime.UtcNow };
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
        }

        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets/{ticket.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(returned);
        Assert.Equal(ticket.Id, returned.Id);
        Assert.Equal(title, returned.Title);
        Assert.Equal(Description, returned.Description);
        Assert.Equal(TicketStatus.Open, returned.Status);
        Assert.Equal(TicketPriority.Medium, returned.Priority);
        Assert.Equal(ticket.CreatedAt.Ticks / 10, returned.CreatedAt.Ticks / 10);
    }

    [Fact]
    public async Task GetTicket_UnknownId_ReturnsNotFound()
    {
        // Los IDs de la aplicación son identity positivos; comprobar la ausencia sin borrar filas.
        const int missingId = -1;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.False(await db.Tickets.AnyAsync(t => t.Id == missingId));
        }

        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets/{missingId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutTicket_UpdatesTextsAndPreservesOtherProperties()
    {
        var original = await CreateTicketAsync(TicketStatus.Resolved, TicketPriority.High);
        var updatedTitle = $"updated-{Guid.NewGuid():N}";
        ownedTitles.Add(updatedTitle);
        const string updatedDescription = "Descripción actualizada por PUT.";
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/tickets/{original.Id}",
            new { title = updatedTitle, description = updatedDescription });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(updated);
        Assert.Equal(updatedTitle, updated.Title);
        Assert.Equal(updatedDescription, updated.Description);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal(original.Status, updated.Status);
        Assert.Equal(original.Priority, updated.Priority);
        await AssertStoredAndReturnedAsync(updated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task PutTicket_InvalidTitle_ReturnsBadRequestWithoutChanges(int titleLength)
    {
        var original = await CreateTicketAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/tickets/{original.Id}",
            new { title = new string('x', titleLength), description = "No debe guardarse." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertStoredAndReturnedAsync(original);
    }

    [Fact]
    public async Task PutTicket_UnknownId_ReturnsNotFound()
    {
        var control = await CreateTicketAsync();
        await AssertMissingIdAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PutAsJsonAsync("/api/tickets/-1",
            new { title, description = Description });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredAndReturnedAsync(control);
    }

    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    public async Task PatchStatus_AllowedTransition_PersistsOnlyStatus(TicketStatus current, TicketStatus requested)
    {
        var original = await CreateTicketAsync(current);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/status",
            new { status = requested.ToString() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(updated);
        original.Status = requested;
        AssertTicketEqual(original, updated);
        await AssertStoredAndReturnedAsync(original);
    }

    [Fact]
    public async Task PatchStatus_OpenToClosed_ReturnsConflictWithoutChanges()
    {
        var original = await CreateTicketAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/status",
            new { status = "Closed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Open", body.RootElement.GetProperty("currentStatus").GetString());
        Assert.Equal("Closed", body.RootElement.GetProperty("requestedStatus").GetString());
        var error = body.RootElement.GetProperty("error").GetString();
        Assert.NotNull(error);
        Assert.Contains("Open", error);
        Assert.Contains("Closed", error);
        await AssertStoredAndReturnedAsync(original);
    }

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public async Task PatchStatus_SameStatus_ReturnsOkWithoutChanges(TicketStatus status)
    {
        var original = await CreateTicketAsync(status);
        using var client = fixture.Factory.CreateClient();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/status",
                new { status = status.ToString() });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
            Assert.NotNull(returned);
            AssertTicketEqual(original, returned);
            await AssertStoredAndReturnedAsync(original);
        }
    }

    [Fact]
    public async Task PatchStatus_InvalidStatus_ReturnsBadRequestWithoutChanges()
    {
        var original = await CreateTicketAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/status",
            new { status = "Inventado" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertStoredAndReturnedAsync(original);
    }

    [Fact]
    public async Task PatchStatus_UnknownId_ReturnsNotFound()
    {
        var control = await CreateTicketAsync();
        await AssertMissingIdAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/tickets/-1/status", new { status = "InProgress" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredAndReturnedAsync(control);
    }

    [Fact]
    public async Task DeleteTicket_RemovesOnlyRequestedTicket()
    {
        var target = await CreateTicketAsync();
        var control = await CreateTicketAsync(TicketStatus.Resolved);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.DeleteAsync($"/api/tickets/{target.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        using var subsequentGet = await client.GetAsync($"/api/tickets/{target.Id}");
        Assert.Equal(HttpStatusCode.NotFound, subsequentGet.StatusCode);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.False(await db.Tickets.AnyAsync(t => t.Id == target.Id));
        }
        await AssertStoredAndReturnedAsync(control);
    }

    [Fact]
    public async Task DeleteTicket_UnknownId_ReturnsNotFoundWithoutRemovingControlTicket()
    {
        var control = await CreateTicketAsync();
        await AssertMissingIdAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.DeleteAsync("/api/tickets/-1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStoredAndReturnedAsync(control);
    }

    private async Task<Ticket> CreateTicketAsync(TicketStatus status = TicketStatus.Open, TicketPriority priority = TicketPriority.Medium)
    {
        var ticket = new Ticket
        {
            Title = $"integration-{Guid.NewGuid():N}", Description = Description,
            CreatedAt = DateTime.UtcNow, Status = status, Priority = priority
        };
        ownedTitles.Add(ticket.Title);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        ownedTicketIds.Add(ticket.Id);
        // Recargar para comparar con la precisión realmente conservada por PostgreSQL.
        await db.Entry(ticket).ReloadAsync();
        return ticket;
    }

    private async Task AssertStoredAndReturnedAsync(Ticket expected)
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets/{expected.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(returned);
        AssertTicketEqual(expected, returned);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == expected.Id);
        AssertTicketEqual(expected, stored);
    }

    private async Task AssertMissingIdAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Tickets.AnyAsync(t => t.Id == -1));
    }

    private static void AssertTicketEqual(Ticket expected, Ticket actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Priority, actual.Priority);
    }

    private static async Task AssertHasErrorAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // xUnit crea una instancia por prueba y ejecuta DisposeAsync aunque falle una aserción.
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var ids = ownedTicketIds.ToArray();
        var titles = ownedTitles.Append(title).ToArray();
        await db.Tickets.Where(t => ids.Contains(t.Id) || titles.Contains(t.Title)).ExecuteDeleteAsync();
        Assert.False(await db.Tickets.AnyAsync(t => ids.Contains(t.Id) || titles.Contains(t.Title)));
    }
}
