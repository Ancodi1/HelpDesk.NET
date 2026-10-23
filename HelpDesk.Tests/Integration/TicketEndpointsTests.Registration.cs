using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    private static string NewTestPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    [Theory]
    [InlineData(null)]
    [InlineData("User")]
    [InlineData("Technician")]
    [InlineData("Admin")]
    [InlineData("0")]
    [InlineData(2)]
    [InlineData(true)]
    public async Task Register_NormalizesEmailForcesUserAndReturnsOnlyPublicDto(object? requestedRole)
    {
        var target = NewUser();
        var password = NewTestPassword();
        var logs = new RegistrationLogs();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient();
        var body = new Dictionary<string, object?>
        {
            ["name"] = target.Name, ["email"] = $"  {target.Email.ToUpperInvariant()}  ",
            ["password"] = password, ["passwordHash"] = NewTestPassword(),
            ["id"] = -1, ["createdAt"] = DateTime.UnixEpoch
        };
        if (requestedRole is not null) body["role"] = requestedRole;
        using var response = await client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var returned = await ReadUserResponseAsync(response);
        Assert.True(returned.Id > 0);
        Assert.Equal(target.Name, returned.Name);
        Assert.Equal(target.Email, returned.Email);
        Assert.Equal(UserRole.User, returned.Role);
        Assert.Equal(DateTimeKind.Utc, returned.CreatedAt.Kind);
        Assert.NotEqual(DateTime.UnixEpoch, returned.CreatedAt);
        Assert.Equal($"/api/users/{returned.Id}", response.Headers.Location?.OriginalString);
        await AssertUserStoredAsync(returned);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == returned.Id);
        Assert.True(!string.IsNullOrEmpty(stored.PasswordHash) && stored.PasswordHash != password);
        Assert.True(stored.PasswordHash != (string)body["passwordHash"]!);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.True(hasher.VerifyHashedPassword(stored, stored.PasswordHash!, password) == PasswordVerificationResult.Success);
        Assert.True(hasher.VerifyHashedPassword(stored, stored.PasswordHash!, NewTestPassword()) == PasswordVerificationResult.Failed);
        using var admin = await CreateAdminClientAsync(factory);
        using var get = await admin.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        await ReadUserResponseAsync(get);
        using var list = await admin.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.All(json.RootElement.EnumerateArray(), u =>
            Assert.Equal(new[] { "createdAt", "email", "id", "name", "role" }, u.EnumerateObject().Select(p => p.Name).OrderBy(n => n)));
        using var duplicate = await client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.NotEmpty(logs.Messages);
        foreach (var message in logs.Messages)
            Assert.True(!message.Contains(password, StringComparison.Ordinal) && !message.Contains(stored.PasswordHash!, StringComparison.Ordinal),
                "Los logs no deben contener la contraseña ni su hash.");
    }

    public static IEnumerable<object[]> InvalidRegistrationBodies()
    {
        yield return ["{}"];
        yield return ["{\"name\":\"Nombre\",\"email\":\"__EMAIL__\"}"];
        foreach (var password in new string?[] { null, "", "   ", new('a', 14), new('a', 129), new string('a', 15) + "\n" })
            yield return [JsonSerializer.Serialize(new { name = "Nombre", email = "__EMAIL__", password })];
        foreach (var name in new string?[] { null, "", "   ", new('n', 101) })
            yield return [JsonSerializer.Serialize(new { name, email = "__EMAIL__", password = "__PASSWORD__" })];
        foreach (var email in new string?[] { null, "", "invalid", "person @example.com", new('a', 255) })
            yield return [JsonSerializer.Serialize(new { name = "Nombre", email, password = "__PASSWORD__" })];
        foreach (var malformed in new[] { "{", "null", "[]", "", "{\"name\":1}", "{\"password\":123}" })
            yield return [malformed];
    }

    [Theory]
    [MemberData(nameof(InvalidRegistrationBodies))]
    public async Task Register_InvalidInput_ReturnsBadRequestWithoutWriting(string payload)
    {
        var target = NewUser();
        using var client = fixture.Factory.CreateClient();
        using var content = new StringContent(payload.Replace("__EMAIL__", target.Email).Replace("__PASSWORD__", NewTestPassword()), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/auth/register", content);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var unexpected = await response.Content.ReadFromJsonAsync<HelpDesk.Api.DTOs.UserResponseDto>(JsonOptions);
            if (unexpected is not null) target.Id = unexpected.Id;
        }
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == target.Email));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(128)]
    public async Task Register_PasswordLengthBoundariesAndExistingFieldLimits_AreAccepted(int length)
    {
        var email = Guid.NewGuid().ToString("N") + new string('a', 32) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 61);
        ownedUserEmails.Add(email);
        var password = " " + new string('á', length - 2) + " ";
        using var client = fixture.Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { name = new string('n', 100), email, password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var returned = await ReadUserResponseAsync(response);
        Assert.Equal(100, returned.Name.Length);
        Assert.Equal(254, returned.Email.Length);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == returned.Id);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        Assert.True(hasher.VerifyHashedPassword(stored, stored.PasswordHash!, password) == PasswordVerificationResult.Success);
    }

    [Fact]
    public async Task Register_DuplicateLegacyEmail_PreservesPasswordlessUsersTicketsAndRelationships()
    {
        var creator = await SeedHttpUserAsync();
        var technician = await SeedHttpUserAsync(UserRole.Technician);
        var ticket = await CreateTicketAsync(createdByUserId: creator.Id, assignedToUserId: technician.Id);
        using var client = fixture.Factory.CreateClient();
        foreach (var existing in new[] { creator, technician })
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", new { existing.Name, email = $" {existing.Email.ToUpperInvariant()} ", password = NewTestPassword() });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertHasErrorAsync(response);
            await AssertUserStoredAsync(new(existing.Id, existing.Name, existing.Email, existing.Role, existing.CreatedAt));
        }
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.All(await db.Users.AsNoTracking().Where(u => u.Id == creator.Id || u.Id == technician.Id).ToListAsync(), u => Assert.Null(u.PasswordHash));
        await AssertStoredAndReturnedAsync(ticket);
    }

    [Fact]
    public async Task Register_ConcurrentDuplicateAfterPrecheck_ReturnsCreatedAndConflict()
    {
        var target = NewUser();
        var barrier = new UserSaveBarrier(target.Email);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<HelpDeskDbContext>(options => options.AddInterceptors(barrier))));
        using var client = factory.CreateClient();
        var password = NewTestPassword();
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/auth/register", new { target.Name, target.Email, password }),
            client.PostAsJsonAsync("/api/auth/register", new { target.Name, email = $" {target.Email.ToUpperInvariant()} ", password }));
        try
        {
            var created = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            await AssertHasErrorAsync(conflict);
            var returned = await ReadUserResponseAsync(created);
            await AssertUserStoredAsync(returned);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var stored = Assert.Single(await db.Users.AsNoTracking().Where(u => u.Email == target.Email).ToListAsync());
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            Assert.True(hasher.VerifyHashedPassword(stored, stored.PasswordHash!, password) == PasswordVerificationResult.Success);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData("POST", "Admin")]
    [InlineData("POST", "Technician")]
    [InlineData("PUT", "Admin")]
    [InlineData("PUT", "User")]
    public async Task UserManagement_NonAdminForbidden_PreservesUsersAndRelations(string method, string role)
    {
        var user = await SeedHttpUserAsync(UserRole.Technician);
        var target = NewUser();
        var ticket = await CreateTicketAsync(createdByUserId: user.Id, assignedToUserId: user.Id);
        using var client = CreateAuthenticatedClient(user);
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/users" : $"/api/users/{user.Id}")
        {
            Content = JsonContent.Create(new { target.Name, target.Email, role, password = NewTestPassword(), passwordHash = NewTestPassword() })
        };
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertUserStoredAsync(new(user.Id, user.Name, user.Email, user.Role, user.CreatedAt));
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == target.Email));
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).PasswordHash);
        await AssertStoredAndReturnedAsync(ticket);
    }

    private sealed class RegistrationLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
