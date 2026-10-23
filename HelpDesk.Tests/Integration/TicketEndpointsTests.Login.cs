using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Admin)]
    public async Task Login_NormalizesEmailUsesStoredRoleAndDoesNotExposeOrLogSecrets(UserRole role)
    {
        var user = await SeedHttpUserAsync(role);
        var password = NewTestPassword();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
            stored.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(stored, password);
            await db.SaveChangesAsync();
            user.PasswordHash = stored.PasswordHash;
        }
        var logs = new RegistrationLogs();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"  {user.Email.ToUpperInvariant()}  ", password, role = "UntrustedRole"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "accessToken", "expiresIn", "tokenType" }, json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        var body = (await response.Content.ReadFromJsonAsync<LoginResponseDto>())!;
        Assert.Equal("Bearer", body.TokenType);
        Assert.Equal(1800, body.ExpiresIn);
        var settings = factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var claims = handler.ValidateToken(body.AccessToken, settings.CreateValidationParameters(), out _);
        Assert.Equal(user.Id.ToString(), claims.FindFirst("sub")?.Value);
        Assert.Equal(user.Email, claims.FindFirst("email")?.Value);
        Assert.Equal(role.ToString(), claims.FindFirst("role")?.Value);
        foreach (var message in logs.Messages)
            Assert.True(!message.Contains(password, StringComparison.Ordinal) &&
                !message.Contains(user.PasswordHash!, StringComparison.Ordinal) && !message.Contains(body.AccessToken, StringComparison.Ordinal),
                "Los logs no deben contener contraseña, hash ni token.");
        await using var readScope = factory.Services.CreateAsyncScope();
        var storedAfter = await readScope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(storedAfter.PasswordHash == user.PasswordHash, "El login no modifica el hash.");
    }

    [Fact]
    public async Task Login_InvalidCredentialsReturnIdenticalGenericUnauthorized()
    {
        var legacy = await SeedHttpUserAsync();
        var registered = NewUser();
        using var client = fixture.Factory.CreateClient();
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new { name = registered.Name, email = registered.Email, password = NewTestPassword() });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        string? expected = null;
        foreach (var email in new[] { legacy.Email, registered.Email, NewUser().Email })
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = NewTestPassword() });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            expected ??= body;
            Assert.Equal(expected, body);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("Credenciales incorrectas.", json.RootElement.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"test@example.invalid\",\"password\":\"\"}")]
    [InlineData("{\"email\":\"test@example.invalid\",\"password\":null}")]
    [InlineData("{\"email\":\"\",\"password\":\"value\"}")]
    [InlineData("{\"email\":null,\"password\":\"value\"}")]
    [InlineData("{\"email\":\"test@example.invalid\",\"password\":\"   \"}")]
    [InlineData("{\"password\":123}")]
    [InlineData("{")]
    [InlineData("null")]
    public async Task Login_EmptyCredentialsOrInvalidJsonReturnBadRequest(string payload)
    {
        using var client = fixture.Factory.CreateClient();
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/auth/login", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertHasErrorAsync(response);
    }

    [Fact]
    public async Task RegisterThenLogin_ProtectsTicketsAndReservesUserManagementForAdmin()
    {
        var user = NewUser();
        var password = NewTestPassword();
        using var client = fixture.Factory.CreateClient();
        using var registered = await client.PostAsJsonAsync("/api/auth/register", new { name = user.Name, email = user.Email, password });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponseDto>())!.AccessToken;
        using var health = await client.GetAsync("/api/health");
        using var tickets = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tickets.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var ownTickets = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.OK, ownTickets.StatusCode);
        using var forbidden = await client.PostAsJsonAsync("/api/users", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenPut = await client.PutAsJsonAsync("/api/users/1", new { role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPut.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-token");
        using var protectedTickets = await client.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedTickets.StatusCode);
    }
}
