using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Demo;
using HelpDesk.Api.Models;
using HelpDesk.Demo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HelpDesk.Tests.Integration;

[Trait("Category", "PostgreSQL")]
public class DemoIntegrationTests(DemoFixture fixture) : IClassFixture<DemoFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.RestoreAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PublicConfiguration_ExposesOnlyTwoPublicCredentialsAndNoAdmin()
    {
        using var client = fixture.Factory.CreateClient();
        using var response = await client.GetAsync("/api/demo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("enabled").GetBoolean());
        var accounts = json.RootElement.GetProperty("accounts").EnumerateArray().ToArray();
        Assert.Equal(2, accounts.Length);
        Assert.Equal(new[] { "User", "Technician" }, accounts.Select(a => a.GetProperty("role").GetString()));
        Assert.True(!body.Contains(fixture.AdminPassword) && !body.Contains(DemoAccounts.AdminEmail));
        Assert.True(!body.Contains(fixture.Target.ConnectionString));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PrivateAdmin_CannotDisableProtectedAccount(int id)
    {
        await using var db = fixture.Db();
        var before = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        using var admin = await fixture.ClientAsync(3);
        using var rejected = await admin.PutAsJsonAsync($"/api/users/{id}", new { name = "Changed", email = "changed@demo.helpdesk.test", role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("demo_account_protected", await rejected.Content.ReadAsStringAsync());
        var after = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.Role, after.Role);
        Assert.True(before.PasswordHash == after.PasswordHash);
        using var anon = fixture.Factory.CreateClient();
        var password = id == 1 ? fixture.UserPassword : id == 2 ? fixture.TechnicianPassword : fixture.AdminPassword;
        using var login = await anon.PostAsJsonAsync("/api/auth/login", new { email = before.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PublicRoles_RetainPermissionsWithoutAccountManagement(int id)
    {
        using var client = await fixture.ClientAsync(id);
        await using var db = fixture.Db();
        var ticket = await db.Tickets.FirstAsync(t => t.CreatedByUserId == 1 && t.AssignedToUserId == 2 && t.Status == TicketStatus.Open);
        using var list = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var forbidden = await client.PutAsJsonAsync($"/api/users/{id}", new { name = "changed" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var priority = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/priority", new { priority = "Critical" });
        Assert.Equal(HttpStatusCode.Forbidden, priority.StatusCode);
        using var status = await client.PatchAsJsonAsync($"/api/tickets/{ticket.Id}/status", new { status = "InProgress" });
        Assert.Equal(id == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK, status.StatusCode);
        using var comment = await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new { content = "Comentario ficticio de validación." });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        using var created = await client.PostAsJsonAsync("/api/tickets", new { title = "Prueba ficticia", description = "Solo datos de laboratorio." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var hidden = await db.Tickets.FirstAsync(t => t.CreatedByUserId == 4 && t.AssignedToUserId == 5);
        using var inaccessible = await client.GetAsync($"/api/tickets/{hidden.Id}");
        Assert.Equal(HttpStatusCode.NotFound, inaccessible.StatusCode);
    }

    [Fact]
    public async Task RegistrationAndAdministrativeCreation_AreClosedButNormalAccountEditingWorks()
    {
        using var client = fixture.Factory.CreateClient();
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new { });
        Assert.Equal(HttpStatusCode.Forbidden, registration.StatusCode);
        using var admin = await fixture.ClientAsync(3);
        using var creation = await admin.PostAsJsonAsync("/api/users", new { });
        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);
        // Una identidad no protegida conserva el PUT administrativo existente.
        using var changed = await admin.PutAsJsonAsync("/api/users/4", new { name = "Equipo de ejemplo actualizado", email = "aurora@demo.helpdesk.test", role = "User" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
    }

    [Fact]
    public async Task Restore_IsRepeatablePreservesCredentialsAndDoesNotReuseTicketIds()
    {
        await using var db = fixture.Db();
        var hashes = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => u.PasswordHash).ToArrayAsync();
        var oldIds = await db.Tickets.Select(t => t.Id).ToArrayAsync();
        using var client = await fixture.ClientAsync(1);
        using var created = await client.PostAsJsonAsync("/api/tickets", new { title = "Ejemplo temporal", description = "Cambio compartido ficticio." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await fixture.RestoreAsync();
        await fixture.RestoreAsync();
        var restoredHashes = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => u.PasswordHash).ToArrayAsync();
        Assert.True(hashes.SequenceEqual(restoredHashes));
        Assert.Equal(8, await db.Tickets.CountAsync());
        Assert.False(await db.Tickets.AnyAsync(t => oldIds.Contains(t.Id)));
        using var oldUrl = await client.GetAsync($"/api/tickets/{oldIds[0]}");
        Assert.Equal(HttpStatusCode.NotFound, oldUrl.StatusCode);
        using var loginClient = fixture.Factory.CreateClient();
        using var login = await loginClient.PostAsJsonAsync("/api/auth/login", new { email = DemoAccounts.UserEmail, password = fixture.UserPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Dataset_RespectsRelationsVisibilityAndHistoricalValues()
    {
        await using var db = fixture.Db();
        var tickets = await db.Tickets.AsNoTracking().ToListAsync();
        Assert.Equal(8, tickets.Count);
        Assert.Equal(4, tickets.Select(t => t.Status).Distinct().Count());
        Assert.Equal(5, tickets.Select(t => t.Category).Distinct().Count());
        Assert.Equal(4, tickets.Select(t => t.Priority).Distinct().Count());
        Assert.Contains(tickets, t => t.AssignedToUserId is null);
        Assert.Contains(tickets, t => t.AssignedToUserId == 2);
        Assert.Equal(14, await db.TicketComments.CountAsync());
        foreach (var ticket in tickets)
        {
            var activities = await db.TicketActivities.Where(a => a.TicketId == ticket.Id).OrderBy(a => a.Id).ToListAsync();
            var statuses = activities.Where(a => a.Field == "Status").ToArray();
            if (statuses.Length > 0) Assert.Equal(ticket.Status.ToString(), statuses[^1].NewValue);
            Assert.All(activities, a => Assert.NotEqual(a.OldValue, a.NewValue));
        }
    }

    [Theory]
    [InlineData("Tickets", 100, "/api/tickets", "POST")]
    [InlineData("TicketComments", 500, "/comments", "POST")]
    [InlineData("TicketActivities", 1000, "/status", "PATCH")]
    public async Task Capacity_IsEnforcedByBackendWhileReadsRemainAvailable(string table, int limit, string suffix, string method)
    {
        await using var db = fixture.Db();
        var ticket = await db.Tickets.FirstAsync(t => t.CreatedByUserId == 1);
        var count = table == "Tickets" ? await db.Tickets.CountAsync() : table == "TicketComments" ? await db.TicketComments.CountAsync() : await db.TicketActivities.CountAsync();
        for (var i = count; i < limit; i++)
        {
            if (table == "Tickets") db.Tickets.Add(new Ticket { Title = "Límite ficticio", Description = "Ejemplo", CreatedAt = DateTime.UtcNow, CreatedByUserId = 1 });
            else if (table == "TicketComments") db.TicketComments.Add(new TicketComment { TicketId = ticket.Id, AuthorUserId = 1, AuthorNameSnapshot = "Visitante de ejemplo", Content = "Ejemplo", CreatedAt = DateTime.UtcNow });
            else db.TicketActivities.Add(new TicketActivity { TicketId = ticket.Id, ActorUserId = 2, ActorNameSnapshot = "Técnica de ejemplo", Field = "Status", OldValue = "Open", NewValue = "InProgress", CreatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        using var client = await fixture.ClientAsync(2);
        var path = table == "Tickets" ? suffix : $"/api/tickets/{ticket.Id}" + suffix;
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { title = "Ejemplo", description = "Ejemplo", content = "Ejemplo", status = "InProgress" }) };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("demo_capacity_reached", await response.Content.ReadAsStringAsync());
        using var read = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await fixture.RestoreAsync();
        using var after = await client.PostAsJsonAsync("/api/tickets", new { title = "Tras restaurar", description = "Ejemplo ficticio" });
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCreation_CannotExceedGlobalCapacity()
    {
        await using var db = fixture.Db();
        for (var i = await db.Tickets.CountAsync(); i < DemoSettings.MaxTickets - 1; i++)
            db.Tickets.Add(new Ticket { Title = "Límite ficticio", Description = "Ejemplo", CreatedAt = DateTime.UtcNow, CreatedByUserId = 1 });
        await db.SaveChangesAsync();
        using var first = await fixture.ClientAsync(1);
        using var second = await fixture.ClientAsync(2);
        var responses = await Task.WhenAll(first.PostAsJsonAsync("/api/tickets", new { title = "Ejemplo concurrente", description = "Ficticio" }),
            second.PostAsJsonAsync("/api/tickets", new { title = "Ejemplo concurrente", description = "Ficticio" }));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(DemoSettings.MaxTickets, await db.Tickets.CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task CancelledRestore_RollsBackPreviouslyDeletedCommentsAndActivities()
    {
        await using var db = fixture.Db();
        var counts = new[] { await db.Tickets.CountAsync(), await db.TicketComments.CountAsync(), await db.TicketActivities.CountAsync() };
        await using var blocker = await fixture.Target.OpenVerifiedAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var block = new NpgsqlCommand("SELECT \"Id\" FROM public.\"Tickets\" ORDER BY \"Id\" LIMIT 1 FOR UPDATE", blocker, transaction);
        await block.ExecuteScalarAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var scope = fixture.Factory.Services.CreateScope();
        var restorer = new DemoRestorer(scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>());
        var restoring = restorer.RestoreAsync(fixture.Target, "RESTORE " + DemoTarget.Database + " " + fixture.Target.InstanceId,
            false, cancellation: cancellation.Token);
        await using var observe = await fixture.Target.OpenVerifiedAsync();
        await using var waiting = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE @blocker = ANY(pg_blocking_pids(pid))", observe);
        waiting.Parameters.AddWithValue("blocker", blocker.ProcessID);
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var blocked = false;
        while (DateTime.UtcNow < deadline && !restoring.IsCompleted)
        {
            if (Convert.ToInt64(await waiting.ExecuteScalarAsync()) > 0) { blocked = true; break; }
            await Task.Delay(20);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restoring);
        Assert.True(blocked, "Se debe observar una espera PostgreSQL real después de borrar dependencias dentro de la transacción.");
        await transaction.RollbackAsync();
        Assert.Equal(counts, new[] { await db.Tickets.CountAsync(), await db.TicketComments.CountAsync(), await db.TicketActivities.CountAsync() });
    }

    [Fact]
    public void Startup_RejectsPublishedCredentialsThatDoNotMatchStoredHashes()
    {
        using var invalid = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<DemoSettings>(options => options.UserPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)))));
        var error = Assert.Throws<InvalidOperationException>(() => invalid.CreateClient());
        Assert.Contains("Las cuentas demo no coinciden", error.Message);
    }

    [Fact]
    public void TurningOffDemo_CannotReuseItsDatabaseThroughDefaultConnection()
    {
        // Comprobar la misma guardia de registro DI con una configuración normal y destino demo.
        using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = fixture.Target.ConnectionString, ["Demo:Enabled"] = "false" }));
            builder.ConfigureTestServices(services => services.AddJwtTestSettings());
        });
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var error = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>());
        Assert.Contains("solo se puede utilizar", error.Message);
    }

    [Fact]
    public async Task WrongMarkerAndSecondInitialization_DoNotDeleteExistingContent()
    {
        await using var db = fixture.Db();
        var before = await db.Tickets.Select(t => t.Id).ToArrayAsync();
        var wrong = new DemoTarget(Environment.GetEnvironmentVariable("HELPDESK_DEMO_TEST_CONNECTION_STRING"), Guid.NewGuid().ToString());
        using var scope = fixture.Factory.Services.CreateScope();
        var restorer = new DemoRestorer(scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restorer.RestoreAsync(wrong,
            "RESTORE " + DemoTarget.Database + " " + wrong.InstanceId, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restorer.RestoreAsync(fixture.Target,
            "RESTORE " + DemoTarget.Database + " " + fixture.Target.InstanceId, true,
            fixture.UserPassword, fixture.TechnicianPassword, fixture.AdminPassword));
        var after = await db.Tickets.Select(t => t.Id).ToArrayAsync();
        Assert.True(before.SequenceEqual(after));
    }
}
