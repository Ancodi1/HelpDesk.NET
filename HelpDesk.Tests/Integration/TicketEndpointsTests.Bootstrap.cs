using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using HelpDesk.AdminBootstrap;
using HelpDesk.Api.Authentication;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    private static BootstrapConnection BootstrapTestTarget() => new(PostgreSqlTestSettings.Validate(
        Environment.GetEnvironmentVariable(PostgreSqlTestSettings.EnvironmentVariable)));

    private async Task RequireNoPreexistingAdminAsync()
    {
        await using var db = BootstrapTestTarget().CreateDbContext();
        Assert.True(!await db.Users.AnyAsync(u => u.Role == UserRole.Admin),
            "Bootstrap requiere helpdesk_test_db sin Admin ajeno a este caso. No se eliminarán ni modificarán cuentas para preparar la prueba.");
    }

    private async Task<BootstrapResult> BootstrapNewAdminAsync(User target, string password)
    {
        var connection = BootstrapTestTarget();
        await using var db = connection.CreateDbContext();
        using var scope = fixture.Factory.Services.CreateScope();
        var service = new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
        return await service.CreateAsync(db, connection, connection.Database, BootstrapService.Confirmation,
            target.Name, target.Email, password, password);
    }

    private async Task<(User[] Users, Ticket[] Tickets)> SeedBootstrapBaselineAsync()
    {
        var user = await SeedHttpUserAsync();
        var technician = await SeedHttpUserAsync(UserRole.Technician);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
            stored.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(stored, NewTestPassword());
            user.PasswordHash = stored.PasswordHash;
            await db.SaveChangesAsync();
        }
        var tickets = new List<Ticket>();
        for (var i = 0; i < 6; i++)
            tickets.Add(await CreateTicketAsync((TicketStatus)(i % 4), (TicketPriority)(i % 4), (TicketCategory)(i % 5),
                i % 3 == 0 ? null : user.Id, i % 2 == 0 ? technician.Id : null));
        return ([user, technician], tickets.ToArray());
    }

    private async Task AssertBootstrapBaselineAsync((User[] Users, Ticket[] Tickets) before)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        foreach (var user in before.Users)
        {
            await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt));
            var hash = await db.Users.Where(u => u.Id == user.Id).Select(u => u.PasswordHash).SingleAsync();
            Assert.True(user.PasswordHash == hash, "El bootstrap no cambia hashes anteriores.");
        }
        foreach (var ticket in before.Tickets)
            AssertTicketEqual(ticket, await db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id));
    }

    [Fact]
    public async Task Bootstrap_FirstAdmin_HashesPasswordLogsInAndAuthorizesWithoutChangingExistingData()
    {
        await RequireNoPreexistingAdminAsync();
        var before = await SeedBootstrapBaselineAsync();
        var target = NewUser();
        var password = NewTestPassword();
        var console = new FakeBootstrapConsole([PostgreSqlTestSettings.DatabaseName,
            $" {target.Email.ToUpperInvariant()} ", target.Name, BootstrapService.Confirmation], [password, password]);
        using var scope = fixture.Factory.Services.CreateScope();
        var command = new BootstrapCommand(console, new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()));
        var connectionString = PostgreSqlTestSettings.Validate(Environment.GetEnvironmentVariable(PostgreSqlTestSettings.EnvironmentVariable));
        Assert.Equal(0, await command.RunAsync([], connectionString));
        await using var readScope = fixture.Factory.Services.CreateAsyncScope();
        var db = readScope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == target.Email);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(!string.IsNullOrEmpty(admin.PasswordHash) && admin.PasswordHash != password);
        Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()
            .VerifyHashedPassword(admin, admin.PasswordHash!, password) == PasswordVerificationResult.Success);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin));
        var logs = new RegistrationLogs();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = target.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        var settings = factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token.AccessToken,
            settings.CreateValidationParameters(), out _);
        Assert.Equal("Admin", principal.FindFirst("role")?.Value);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
        using var directory = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
        // Se prueba una escritura administrativa únicamente con una cuenta nueva del propio caso.
        var managed = NewUser();
        using var created = await client.PostAsJsonAsync("/api/users", new { managed.Name, managed.Email, role = "Technician" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ReadUserResponseAsync(created);
        var messages = string.Join('\n', logs.Messages) + console.Output + await login.Content.ReadAsStringAsync();
        foreach (var sensitive in new[] { password, admin.PasswordHash!, connectionString,
                     new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Password! })
            Assert.True(!messages.Contains(sensitive, StringComparison.Ordinal), "No exponer contraseñas, hashes ni conexiones.");
        Assert.All(logs.Messages, message => Assert.True(!message.Contains(token.AccessToken, StringComparison.Ordinal), "No registrar tokens."));
        await AssertBootstrapBaselineAsync(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bootstrap_ExistingAdminWithOrWithoutPassword_RejectsWithoutChangingAccounts(bool withPassword)
    {
        await RequireNoPreexistingAdminAsync();
        var existing = await SeedHttpUserAsync(UserRole.Admin);
        if (withPassword)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var stored = await db.Users.SingleAsync(u => u.Id == existing.Id);
            stored.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(stored, NewTestPassword());
            existing.PasswordHash = stored.PasswordHash;
            await db.SaveChangesAsync();
        }
        var candidate = NewUser();
        Assert.Equal(BootstrapResult.AdminExists, await BootstrapNewAdminAsync(candidate, NewTestPassword()));
        // Tampoco se permite restablecer la credencial del propio Admin.
        Assert.Equal(BootstrapResult.AdminExists, await BootstrapNewAdminAsync(existing, NewTestPassword()));
        await using var checkScope = fixture.Factory.Services.CreateAsyncScope();
        var context = checkScope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await context.Users.AnyAsync(u => u.Email == candidate.Email));
        var after = await context.Users.AsNoTracking().SingleAsync(u => u.Id == existing.Id);
        Assert.True(after.PasswordHash == existing.PasswordHash, "Un segundo bootstrap no modifica credenciales.");
        Assert.Equal(1, await context.Users.CountAsync(u => u.Role == UserRole.Admin));
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    public async Task Bootstrap_DuplicateEmailNeverPromotesOrActivatesExistingAccount(UserRole role)
    {
        await RequireNoPreexistingAdminAsync();
        var before = await SeedBootstrapBaselineAsync();
        var target = before.Users.Single(u => u.Role == role);
        var candidate = new User { Name = "Must not change", Email = $" {target.Email.ToUpperInvariant()} " };
        Assert.Equal(BootstrapResult.EmailExists, await BootstrapNewAdminAsync(candidate, NewTestPassword()));
        await AssertBootstrapBaselineAsync(before);
        await using var db = BootstrapTestTarget().CreateDbContext();
        Assert.False(await db.Users.AnyAsync(u => u.Role == UserRole.Admin));
    }

    [Fact]
    public async Task Bootstrap_IncorrectConfirmationPreservesPreviousUsersAndTickets()
    {
        await RequireNoPreexistingAdminAsync();
        var before = await SeedBootstrapBaselineAsync();
        var target = NewUser();
        var password = NewTestPassword();
        using var scope = fixture.Factory.Services.CreateScope();
        var console = new FakeBootstrapConsole([PostgreSqlTestSettings.DatabaseName, target.Email, target.Name, "incorrect"], [password, password]);
        var command = new BootstrapCommand(console, new BootstrapService(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()));
        Assert.Equal(2, await command.RunAsync([], PostgreSqlTestSettings.Validate(
            Environment.GetEnvironmentVariable(PostgreSqlTestSettings.EnvironmentVariable))));
        await AssertBootstrapBaselineAsync(before);
        await using var db = BootstrapTestTarget().CreateDbContext();
        Assert.False(await db.Users.AnyAsync(u => u.Email == target.Email || u.Role == UserRole.Admin));
    }

    [Fact]
    public async Task Bootstrap_TwoRealCliProcesses_SerializeAndOnlyOneCreatesAnAdmin()
    {
        await RequireNoPreexistingAdminAsync();
        var before = await SeedBootstrapBaselineAsync();
        var firstUser = NewUser();
        var secondUser = NewUser();
        var firstPassword = NewTestPassword();
        var secondPassword = NewTestPassword();
        var connectionString = PostgreSqlTestSettings.Validate(Environment.GetEnvironmentVariable(PostgreSqlTestSettings.EnvironmentVariable));
        var target = BootstrapTestTarget();
        await using var blocker = target.CreateDbContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Users\" IN SHARE ROW EXCLUSIVE MODE");
        await using var first = new BootstrapTerminalProcess(connectionString);
        await using var second = new BootstrapTerminalProcess(connectionString);
        await Task.WhenAll(first.ConfirmAsync(firstUser.Email, firstUser.Name, firstPassword),
            second.ConfirmAsync(secondUser.Email, secondUser.Name, secondPassword));

        // Ambos procesos deben estar esperando el bloqueo real antes de soltarlo, no solo arrancados.
        await using var observer = target.CreateDbContext();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            while (true)
            {
                var waiting = await observer.Database.SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value"
                    FROM pg_locks l JOIN pg_stat_activity a ON a.pid = l.pid
                    WHERE a.datname = current_database() AND a.usename = current_user
                      AND a.application_name = {BootstrapConnection.ApplicationName}
                      AND l.relation = 'public."Users"'::regclass
                      AND l.mode = 'ShareRowExclusiveLock' AND NOT l.granted
                    """).SingleAsync(timeout.Token);
                if (waiting == 2) break;
                await Task.Yield();
            }
        }
        await transaction.CommitAsync();
        var results = await Task.WhenAll(first.CompleteAsync(), second.CompleteAsync());
        Assert.Equal(new[] { 0, 2 }, results.Select(r => r.ExitCode).Order());
        await using var db = target.CreateDbContext();
        var candidates = new[] { firstUser.Email, secondUser.Email };
        var winner = Assert.Single(await db.Users.AsNoTracking().Where(u => candidates.Contains(u.Email)).ToListAsync());
        Assert.Equal(UserRole.Admin, winner.Role);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Role == UserRole.Admin));
        using var scope = fixture.Factory.Services.CreateScope();
        var winningPassword = winner.Email == firstUser.Email ? firstPassword : secondPassword;
        Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>()
            .VerifyHashedPassword(winner, winner.PasswordHash!, winningPassword) == PasswordVerificationResult.Success);
        foreach (var result in results)
            foreach (var secret in new[] { firstPassword, secondPassword, winner.PasswordHash!, connectionString,
                         new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Password! })
                Assert.True(!result.Output.Contains(secret, StringComparison.Ordinal), "La terminal CLI no debe mostrar secretos.");
        await AssertBootstrapBaselineAsync(before);
    }
}
