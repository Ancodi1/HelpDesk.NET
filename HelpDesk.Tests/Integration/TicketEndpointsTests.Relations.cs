using System.Net;
using System.Net.Http.Json;
using System.Text;
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
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task PostTicket_CreatorOfAnyRole_IsPersisted(UserRole role)
    {
        var user = await SeedHttpUserAsync(role);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/tickets", new
        {
            title, description = Description, createdByUserId = user.Id,
            assignedToUserId = user.Id // Crear no asigna, aunque lleguen campos adicionales.
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(ticket);
        ownedTicketIds.Add(ticket.Id);
        Assert.Equal(user.Id, ticket.CreatedByUserId);
        Assert.Null(ticket.AssignedToUserId);
        await AssertNoUserNavigationsAsync(response);
        // Recargar por precisión de PostgreSQL antes de la comparación compartida.
        ticket.CreatedAt = new DateTime(ticket.CreatedAt.Ticks / 10 * 10, DateTimeKind.Utc);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostTicket_LegacyOrExplicitNullCreator_RemainsCompatible(bool explicitNull)
    {
        var body = new Dictionary<string, object?> { ["title"] = title, ["description"] = Description };
        if (explicitNull) body["createdByUserId"] = null;
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/tickets", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(ticket);
        ownedTicketIds.Add(ticket.Id);
        Assert.Null(ticket.CreatedByUserId);
        Assert.Null(ticket.AssignedToUserId);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Null(stored.CreatedByUserId);
        Assert.Null(stored.AssignedToUserId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task PostTicket_MissingCreator_ReturnsBadRequestWithoutWriting(int creatorId)
    {
        await AssertAbsentUserAsync(creatorId);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/tickets", new { title, description = Description, createdByUserId = creatorId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Tickets.AnyAsync(t => t.Title == title));
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task Assignment_ValidRole_OnlyChangesAssigneeAndIsRepeatable(UserRole role)
    {
        var creator = await SeedHttpUserAsync();
        var assignee = await SeedHttpUserAsync(role);
        var original = await CreateTicketAsync(TicketStatus.Resolved, TicketPriority.High, TicketCategory.Network, creator.Id);
        using var client = fixture.Factory.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{original.Id}/assignment", new
            {
                assignedToUserId = assignee.Id, createdByUserId = -1, title = "ignored", description = "ignored",
                status = "Closed", priority = "Low", category = "Other", createdAt = DateTime.UnixEpoch
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
            Assert.NotNull(returned);
            original.AssignedToUserId = assignee.Id;
            AssertTicketEqual(original, returned);
            await AssertNoUserNavigationsAsync(response);
            await AssertStoredAndReturnedAsync(original);
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var withUsers = await db.Tickets.Include(t => t.CreatedByUser).Include(t => t.AssignedToUser)
            .SingleAsync(t => t.Id == original.Id);
        Assert.Equal(creator.Id, withUsers.CreatedByUser!.Id);
        Assert.Equal(assignee.Id, withUsers.AssignedToUser!.Id);
    }

    [Fact]
    public async Task Assignment_UserRole_IsRejectedWithoutChanges()
    {
        var user = await SeedHttpUserAsync();
        var ticket = await CreateTicketAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = user.Id });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Assignment_MissingUser_IsRejectedWithoutChanges(int userId)
    {
        await AssertAbsentUserAsync(userId);
        var valid = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(assignedToUserId: valid.Id);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = userId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Fact]
    public async Task Assignment_ReassignAndUnassign_PreservesCreatorAndOtherFields()
    {
        var first = await SeedHttpUserAsync(UserRole.Technician);
        var second = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync(createdByUserId: first.Id, assignedToUserId: first.Id);
        using var client = fixture.Factory.CreateClient();
        foreach (int? target in new int?[] { second.Id, null, null })
        {
            using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = target });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
            Assert.NotNull(returned);
            ticket.AssignedToUserId = target;
            AssertTicketEqual(ticket, returned);
            await AssertStoredAndReturnedAsync(ticket);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"assignedToUserId\":\"5\"}")]
    [InlineData("{\"assignedToUserId\":1.5}")]
    [InlineData("{\"assignedToUserId\":2147483648}")]
    [InlineData("{\"assignedToUserId\":true}")]
    [InlineData("{\"assignedToUserId\":")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task Assignment_InvalidBody_ReturnsJsonBadRequestWithoutChanges(string payload)
    {
        var ticket = await CreateTicketAsync();
        using var client = fixture.Factory.CreateClient();
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PatchAsync($"/api/tickets/{ticket.Id}/assignment", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Fact]
    public async Task Assignment_MissingTicket_ReturnsNotFound()
    {
        await AssertMissingIdAsync();
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PatchAsJsonAsync("/api/tickets/-1/assignment", new { assignedToUserId = (int?)null });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("status")]
    [InlineData("priority")]
    [InlineData("category")]
    public async Task OrdinaryTicketUpdates_PreserveBothRelationships(string endpoint)
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(createdByUserId: user.Id, assignedToUserId: user.Id);
        using var client = fixture.Factory.CreateClient();
        var body = new { title = "Texto actualizado", description = "Nueva descripción", status = "InProgress", priority = "Critical", category = "Access", createdByUserId = -1, assignedToUserId = -1 };
        using var response = endpoint == "PUT"
            ? await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}", body)
            : await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{endpoint}", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (endpoint == "PUT") { ticket.Title = body.title; ticket.Description = body.description; }
        if (endpoint == "status") ticket.Status = TicketStatus.InProgress;
        if (endpoint == "priority") ticket.Priority = TicketPriority.Critical;
        if (endpoint == "category") ticket.Category = TicketCategory.Access;
        var returned = await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions);
        Assert.NotNull(returned);
        AssertTicketEqual(ticket, returned);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Fact]
    public async Task RelatedAndLegacyTickets_KeepFilteringPaginationAndResponseShape()
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var legacy = await CreateTicketAsync();
        var related = await CreateTicketAsync(createdByUserId: user.Id, assignedToUserId: user.Id);
        using var client = fixture.Factory.CreateClient();
        // Títulos exactos de los dos tickets propios: sin depender de otras filas.
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            foreach (var ticket in new[] { legacy, related })
            {
                var stored = await db.Tickets.SingleAsync(t => t.Id == ticket.Id);
                stored.Description = title;
                ticket.Description = title;
            }
            await db.SaveChangesAsync();
        }
        var ids = new HashSet<int>();
        for (var page = 1; page <= 2; page++)
        {
            using var response = await client.GetAsync($"/api/tickets?search={title}&status=Open&priority=Medium&category=Other&pageSize=1&page={page}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<TicketPageDto>(JsonOptions);
            Assert.NotNull(result);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal(2, result.TotalPages);
            var returned = Assert.Single(result.Items);
            Assert.True(ids.Add(returned.Id));
            AssertTicketEqual(returned.Id == legacy.Id ? legacy : related, returned);
        }
        Assert.Equal(2, ids.Count);
        await AssertStoredAndReturnedAsync(legacy);
        await AssertStoredAndReturnedAsync(related);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Database_ForeignKeysRejectUnknownUsers(bool creator)
    {
        await AssertAbsentUserAsync(-1);
        var ticket = await CreateTicketAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Tickets.SingleAsync(t => t.Id == ticket.Id);
        if (creator) stored.CreatedByUserId = -1;
        else stored.AssignedToUserId = -1;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Database_DeletingReferencedUser_IsRestrictedAndPreservesTicket(bool creator)
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(createdByUserId: creator ? user.Id : null, assignedToUserId: creator ? null : user.Id);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        Assert.True(await db.Users.AnyAsync(u => u.Id == user.Id));
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task PutUser_AssignedUserCannotBeDowngradedUntilUnassigned(UserRole initial)
    {
        var user = await SeedHttpUserAsync(initial);
        var ticket = await CreateTicketAsync(assignedToUserId: user.Id);
        using var client = fixture.Factory.CreateClient();
        using var conflict = await client.PutAsJsonAsync($"/api/users/{user.Id}", new { name = "No se guarda", user.Email, role = "User" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertHasErrorAsync(conflict);
        await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, initial, user.CreatedAt));
        await AssertStoredAndReturnedAsync(ticket);
        using var unassign = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/assignment", new { assignedToUserId = (int?)null });
        Assert.Equal(HttpStatusCode.OK, unassign.StatusCode);
        using var success = await client.PutAsJsonAsync($"/api/users/{user.Id}", new { user.Name, user.Email, role = "User" });
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, UserRole.User, user.CreatedAt));
    }

    [Fact]
    public async Task PutUser_CreatorOnly_CanChangeRole()
    {
        var user = await SeedHttpUserAsync(UserRole.Admin);
        var ticket = await CreateTicketAsync(createdByUserId: user.Id);
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/users/{user.Id}", new { user.Name, user.Email, role = "User" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Fact]
    public async Task TicketCreator_DeletedAfterValidation_ReturnsConflict()
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var pause = new PauseTicketSave(title);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(pause))));
        using var client = factory.CreateClient();
        var pending = client.PostAsJsonAsync("/api/tickets", new { title, description = Description, createdByUserId = user.Id });
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(1, await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync());
            pause.Resume.TrySetResult();
            using var response = await pending;
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertHasErrorAsync(response);
        }
        finally
        {
            pause.Resume.TrySetResult();
            // Drenar la petición antes de limpiar, también si falla una aserción.
            (await pending).Dispose();
        }
        await using var verification = fixture.Factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await context.Tickets.AnyAsync(t => t.Title == title));
    }

    private sealed class PauseTicketSave(string title) : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Ticket>().Any(e => e.Entity.Title == title))
            {
                Reached.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private async Task AssertAbsentUserAsync(int id)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == id));
    }

    private static async Task AssertNoUserNavigationsAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("createdByUserId", out _));
        Assert.True(json.RootElement.TryGetProperty("assignedToUserId", out _));
        Assert.False(json.RootElement.TryGetProperty("createdByUser", out _));
        Assert.False(json.RootElement.TryGetProperty("assignedToUser", out _));
    }
}
