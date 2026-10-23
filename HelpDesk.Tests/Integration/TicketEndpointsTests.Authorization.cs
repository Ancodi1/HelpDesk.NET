using System.Net;
using System.Net.Http.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task Authorization_VisibilityFiltersCountsAndPaginationIncludingHistoricalTickets(UserRole role)
    {
        var actor = await SeedHttpUserAsync(role);
        var other = await SeedHttpUserAsync();
        var own = await CreateTicketAsync(createdByUserId: actor.Id);
        var foreign = await CreateTicketAsync(createdByUserId: other.Id);
        var historical = await CreateTicketAsync();
        var assigned = await CreateTicketAsync(assignedToUserId: role == UserRole.User ? null : actor.Id);
        var all = new[] { own, foreign, historical, assigned };
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var ids = all.Select(t => t.Id).ToArray();
            await db.Tickets.Where(t => ids.Contains(t.Id)).ExecuteUpdateAsync(updates => updates.SetProperty(t => t.Description, title));
        }
        using var client = CreateAuthenticatedClient(actor);
        var visible = role switch
        {
            UserRole.Admin => all.Select(t => t.Id).ToHashSet(),
            UserRole.Technician => new HashSet<int> { own.Id, assigned.Id },
            _ => new HashSet<int> { own.Id }
        };
        var collected = new List<int>();
        for (var page = 1; page <= visible.Count + 1; page++)
        {
            using var response = await client.GetAsync($"/api/tickets?search={title}&status=Open&priority=Medium&category=Other&pageSize=1&page={page}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<TicketPageDto>(JsonOptions))!;
            Assert.Equal(visible.Count, result.TotalCount);
            Assert.Equal(visible.Count, result.TotalPages);
            if (page <= visible.Count) collected.Add(Assert.Single(result.Items).Id);
            else Assert.Empty(result.Items);
        }
        Assert.Equal(visible.OrderDescending(), collected);
        foreach (var ticket in all)
        {
            using var response = await client.GetAsync($"/api/tickets/{ticket.Id}");
            Assert.Equal(visible.Contains(ticket.Id) ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
            if (!visible.Contains(ticket.Id)) Assert.Empty(await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task Authorization_CreationIgnoresForgedCreatorAndAssigneeForEveryRole(UserRole role)
    {
        var actor = await SeedHttpUserAsync(role);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        using var client = CreateAuthenticatedClient(actor);
        using var response = await client.PostAsJsonAsync("/api/tickets", new
        {
            title, description = Description, createdByUserId = other.Id,
            assignedToUserId = other.Id, role = "Admin"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = (await response.Content.ReadFromJsonAsync<Ticket>(JsonOptions))!;
        Assert.Equal(actor.Id, ticket.CreatedByUserId);
        Assert.Null(ticket.AssignedToUserId);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Theory]
    [InlineData(UserRole.User, "PUT")]
    [InlineData(UserRole.User, "DELETE")]
    [InlineData(UserRole.User, "status")]
    [InlineData(UserRole.User, "priority")]
    [InlineData(UserRole.User, "category")]
    [InlineData(UserRole.User, "assignment")]
    [InlineData(UserRole.Technician, "PUT")]
    [InlineData(UserRole.Technician, "DELETE")]
    [InlineData(UserRole.Technician, "priority")]
    [InlineData(UserRole.Technician, "category")]
    [InlineData(UserRole.Technician, "assignment")]
    public async Task Authorization_NonAdminCannotManageTicketsEvenOwnedOrAssigned(UserRole role, string operation)
    {
        var actor = await SeedHttpUserAsync(role);
        var ticket = await CreateTicketAsync(createdByUserId: actor.Id,
            assignedToUserId: role == UserRole.Technician ? actor.Id : null);
        using var client = CreateAuthenticatedClient(actor);
        using var response = operation switch
        {
            "PUT" => await client.PutAsJsonAsync($"/api/tickets/{ticket.Id}", new { title = "ignored", description = "ignored" }),
            "DELETE" => await client.DeleteAsync($"/api/tickets/{ticket.Id}"),
            _ => await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/{operation}", new
            {
                status = "InProgress", priority = "Critical", category = "Access", assignedToUserId = (int?)null
            })
        };
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Theory]
    [InlineData("assigned", HttpStatusCode.OK)]
    [InlineData("historical-assigned", HttpStatusCode.OK)]
    [InlineData("own", HttpStatusCode.Forbidden)]
    [InlineData("foreign", HttpStatusCode.NotFound)]
    [InlineData("historical", HttpStatusCode.NotFound)]
    [InlineData("other-assignee", HttpStatusCode.NotFound)]
    public async Task Authorization_TechnicianChangesOnlyAssignedStatus(string scenario, HttpStatusCode expected)
    {
        var technician = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(
            createdByUserId: scenario == "own" ? technician.Id : scenario.StartsWith("historical") ? null : other.Id,
            assignedToUserId: scenario is "assigned" or "historical-assigned" ? technician.Id : scenario == "other-assignee" ? other.Id : null);
        using var client = CreateAuthenticatedClient(technician);
        using var response = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "InProgress" });
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK) ticket.Status = TicketStatus.InProgress;
        else Assert.Empty(await response.Content.ReadAsStringAsync());
        await AssertStoredAndReturnedAsync(ticket);
        if (expected == HttpStatusCode.OK)
        {
            using var invalidTransition = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "Closed" });
            Assert.Equal(HttpStatusCode.Conflict, invalidTransition.StatusCode);
            await AssertStoredAndReturnedAsync(ticket);
        }
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    public async Task Authorization_ProfileIsPrivateAndUserAdministrationForbidden(UserRole role)
    {
        var actor = await SeedHttpUserAsync(role);
        var other = await SeedHttpUserAsync();
        using var client = CreateAuthenticatedClient(actor);
        using var own = await client.GetAsync($"/api/users/{actor.Id}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(actor.Id, (await ReadUserResponseAsync(own)).Id);
        using var foreign = await client.GetAsync($"/api/users/{other.Id}");
        using var absent = await client.GetAsync("/api/users/-1");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Equal(await absent.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        using var directory = await client.GetAsync("/api/users");
        using var post = await client.PostAsJsonAsync("/api/users", new { other.Name, other.Email, role = "Admin" });
        using var put = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new { actor.Name, actor.Email, role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, directory.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        await AssertUserStoredAsync(new(actor.Id, actor.Name, actor.Email, actor.Role, actor.CreatedAt));
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("demoted")]
    [InlineData("promoted")]
    [InlineData("forged-role")]
    public async Task Authorization_DeletedAccountOrChangedRoleCannotReuseToken(string scenario)
    {
        var actor = await SeedHttpUserAsync(scenario is "promoted" or "forged-role" ? UserRole.User : UserRole.Admin);
        var tokenAccount = scenario == "forged-role"
            ? new User { Id = actor.Id, Email = actor.Email, Role = UserRole.Admin }
            : actor;
        using var client = CreateAuthenticatedClient(tokenAccount);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            if (scenario == "deleted") await db.Users.Where(u => u.Id == actor.Id).ExecuteDeleteAsync();
            else if (scenario != "forged-role")
            {
                var currentRole = scenario == "promoted" ? UserRole.Admin : UserRole.User;
                await db.Users.Where(u => u.Id == actor.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.Role, currentRole));
            }
        }
        // Incluso un token con firma válida y role Admin se contrasta con el rol real.
        foreach (var route in new[] { "/api/users", "/api/tickets", $"/api/users/{actor.Id}" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task AdminUserManagement_ValidatesPreservesCredentialsAndAssignmentRestriction()
    {
        var target = NewUser(UserRole.Technician);
        using var client = await CreateAdminClientAsync();
        using var created = await client.PostAsJsonAsync("/api/users", new { target.Name, target.Email, role = "Technician", password = NewTestPassword() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await ReadUserResponseAsync(created);
        target.Id = dto.Id;
        await AssertUserStoredAsync(dto);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Null((await db.Users.SingleAsync(u => u.Id == dto.Id)).PasswordHash);
        }
        using var duplicate = await client.PostAsJsonAsync("/api/users", new { target.Name, email = $" {target.Email.ToUpperInvariant()} ", role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var assigned = await CreateTicketAsync(assignedToUserId: dto.Id);
        using var rejected = await client.PutAsJsonAsync($"/api/users/{dto.Id}", new { name = "must not change", target.Email, role = "User" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await AssertUserStoredAsync(dto);
        using var promoted = await client.PutAsJsonAsync($"/api/users/{dto.Id}", new { target.Name, target.Email, role = "Admin", passwordHash = NewTestPassword() });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var after = await ReadUserResponseAsync(promoted);
        Assert.Equal(UserRole.Admin, after.Role);
        Assert.Equal(dto.CreatedAt, after.CreatedAt);
        await AssertUserStoredAsync(after);
        foreach (var bad in new[] { "0", "admin", "Invalid" })
        {
            using var invalid = await client.PutAsJsonAsync($"/api/users/{dto.Id}", new { target.Name, target.Email, role = bad });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await using var checkScope = fixture.Factory.Services.CreateAsyncScope();
        var stored = await checkScope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == dto.Id);
        Assert.Null(stored.PasswordHash);
        await AssertStoredAndReturnedAsync(assigned);
    }

    [Fact]
    public async Task AdminUserManagement_DuplicateAndInvalidUpdatesPreserveHashAndFields()
    {
        var actor = await SeedHttpUserAsync(UserRole.Technician);
        var other = await SeedHttpUserAsync();
        string originalHash;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var stored = await db.Users.SingleAsync(u => u.Id == actor.Id);
            originalHash = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>()
                .HashPassword(stored, NewTestPassword());
            stored.PasswordHash = originalHash;
            await db.SaveChangesAsync();
        }
        using var client = await CreateAdminClientAsync();
        using var duplicate = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new { actor.Name, other.Email, role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var invalid = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new { name = "", actor.Email, role = "Admin" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await AssertUserStoredAsync(new(actor.Id, actor.Name, actor.Email, actor.Role, actor.CreatedAt));
        using var updated = await client.PutAsJsonAsync($"/api/users/{actor.Id}", new
        {
            actor.Name, email = $" {actor.Email.ToUpperInvariant()} ", role = "Admin", passwordHash = NewTestPassword(),
            id = other.Id, createdAt = DateTime.UnixEpoch
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var dto = await ReadUserResponseAsync(updated);
        Assert.Equal(actor.Id, dto.Id);
        Assert.Equal(actor.Email, dto.Email);
        Assert.Equal(actor.CreatedAt, dto.CreatedAt);
        Assert.Equal(UserRole.Admin, dto.Role);
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var storedAfter = await check.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == actor.Id);
        Assert.True(originalHash == storedAfter.PasswordHash, "El PUT administrativo no cambia credenciales.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminUserManagement_ConcurrentUniquenessRemainsEnforced(bool update)
    {
        var target = NewUser();
        var first = update ? await SeedHttpUserAsync() : null;
        var second = update ? await SeedHttpUserAsync() : null;
        var barrier = new UserSaveBarrier(target.Email);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(barrier))));
        using var client = await CreateAdminClientAsync(factory);
        var body = new { target.Name, target.Email, role = "User" };
        var responses = await Task.WhenAll(
            update ? client.PutAsJsonAsync($"/api/users/{first!.Id}", body) : client.PostAsJsonAsync("/api/users", body),
            update ? client.PutAsJsonAsync($"/api/users/{second!.Id}", body) : client.PostAsJsonAsync("/api/users", body));
        try
        {
            Assert.Single(responses, r => r.StatusCode == (update ? HttpStatusCode.OK : HttpStatusCode.Created));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == target.Email));
            if (update)
            {
                var loser = responses[0].StatusCode == HttpStatusCode.Conflict ? first! : second!;
                await AssertUserStoredAsync(new(loser.Id, loser.Name, loser.Email, loser.Role, loser.CreatedAt));
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

}
