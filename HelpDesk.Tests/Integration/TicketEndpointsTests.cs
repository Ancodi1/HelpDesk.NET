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
public class TicketEndpointsTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly string title = $"integration-{Guid.NewGuid():N}";
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
        Assert.Equal(DateTimeKind.Utc, ticket.CreatedAt.Kind);
        Assert.NotEqual(default, ticket.CreatedAt);
        Assert.Equal($"/api/tickets/{ticket.Id}", response.Headers.Location?.OriginalString);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(title, stored.Title);
        Assert.Equal(Description, stored.Description);
        Assert.Equal(TicketStatus.Open, stored.Status);
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

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // xUnit crea una instancia por prueba y ejecuta DisposeAsync aunque falle una aserción.
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        await db.Tickets.Where(t => t.Title == title).ExecuteDeleteAsync();
        Assert.False(await db.Tickets.AnyAsync(t => t.Title == title));
    }
}
