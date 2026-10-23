using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using HelpDesk.Api.Validation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HelpDesk.Tests;

public class RegistrationSecurityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("               ")]
    [InlineData("corta")]
    [InlineData("12345678901234")]
    [InlineData("12345678901234\n")]
    [InlineData("12345678901234\0")]
    public void PasswordPolicy_RejectsInvalidValues(string? password) =>
        Assert.NotNull(PasswordValidation.Validate(password));

    [Fact]
    public void PasswordPolicy_EnforcesBoundsWithoutCompositionRules()
    {
        Assert.Null(PasswordValidation.Validate(new string('a', 15)));
        Assert.Null(PasswordValidation.Validate(new string('á', 128)));
        Assert.Null(PasswordValidation.Validate(" frase larga con espacios "));
        Assert.NotNull(PasswordValidation.Validate(new string('a', 129)));
    }

    [Fact]
    public void ConfiguredHasher_UsesSaltAndVerifiesExactPassword()
    {
        using var factory = Factory();
        using var scope = factory.Services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;
        Assert.Equal(PasswordHasherCompatibilityMode.IdentityV3, options.CompatibilityMode);
        Assert.Equal(210_000, options.IterationCount);
        var password = " " + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + " ";
        var user = new User();
        var hash = hasher.HashPassword(user, password);
        Assert.True(hash != password);
        Assert.True(hash != hasher.HashPassword(user, password));
        Assert.True(hasher.VerifyHashedPassword(user, hash, password) == PasswordVerificationResult.Success);
        Assert.True(hasher.VerifyHashedPassword(user, hash, password.Trim()) == PasswordVerificationResult.Failed);
    }

    [Fact]
    public void ModelAndPublicContracts_KeepHashNullableAndPrivate()
    {
        using var db = new HelpDeskDbContext(new DbContextOptionsBuilder<HelpDeskDbContext>()
            .UseNpgsql("Host=localhost;Database=helpdesk_test_db").Options);
        var property = db.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.PasswordHash))!;
        Assert.True(property.IsNullable);
        Assert.Equal("text", property.GetColumnType());
        var user = new User();
        Assert.Null(user.PasswordHash);
        user.PasswordHash = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(user, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("passwordHash", out _));
        Assert.DoesNotContain(typeof(UserResponseDto).GetProperties(), p => p.Name is "PasswordHash" or "Password");
        Assert.Equal(new[] { "Email", "Name", "Password" }, typeof(RegisterUserDto).GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Theory]
    [InlineData("Testing", "POST", "/api/users")]
    [InlineData("Testing", "PUT", "/api/users/1")]
    [InlineData("Development", "POST", "/api/users")]
    [InlineData("Development", "PUT", "/api/users/-1")]
    [InlineData("Production", "POST", "/api/users")]
    [InlineData("Production", "PUT", "/api/users/1")]
    public async Task UserManagement_IsDisabledWithoutResolvingDatabase(string environment, string method, string path)
    {
        using var factory = Factory(environment);
        using var client = factory.CreateClient();
        foreach (var payload in new[] { "{\"role\":\"Admin\"}", "{\"role\":\"Technician\"}", "{", "" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.TryGetProperty("error", out _));
        }
    }

    private static WebApplicationFactory<Program> Factory(string environment = "Testing") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
}
