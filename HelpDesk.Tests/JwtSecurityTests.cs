using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using HelpDesk.Api.Authentication;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Tests;

public class JwtSecurityTests
{
    [Fact]
    public void IssuedToken_HasExpectedClaimsSignatureAndThirtyMinuteLifetime()
    {
        var settings = JwtTestSettings.Create();
        var now = DateTimeOffset.UtcNow;
        var clock = new FixedClock(now);
        var issuer = new JwtTokenIssuer(Options.Create(settings), clock);
        var user = new User { Id = 42, Email = " TEST@EXAMPLE.INVALID ", Role = UserRole.Technician };
        var response = issuer.Issue(user);
        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(1800, response.ExpiresIn);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(response.AccessToken, settings.CreateValidationParameters(), out var validated);
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal("42", principal.FindFirstValue("sub"));
        Assert.Equal("test@example.invalid", principal.FindFirstValue("email"));
        Assert.Equal("Technician", principal.FindFirstValue("role"));
        Assert.Equal(settings.Issuer, jwt.Issuer);
        Assert.Equal(settings.Audience, Assert.Single(jwt.Audiences));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()).UtcDateTime, jwt.IssuedAt);
        Assert.Equal(TimeSpan.FromMinutes(30), jwt.ValidTo - jwt.IssuedAt);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    // Usa el handler Bearer registrado en Program, sin añadir un endpoint a la aplicación.
    [Theory]
    [InlineData("valid", true)]
    [InlineData("signature", false)]
    [InlineData("tampered", false)]
    [InlineData("expired", false)]
    [InlineData("issuer", false)]
    [InlineData("audience", false)]
    [InlineData("unsigned", false)]
    [InlineData("algorithm", false)]
    [InlineData("future", false)]
    [InlineData("missing-exp", false)]
    public async Task BearerHandler_ValidatesRealTokens(string scenario, bool expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddJwtTestSettings());
        });
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<JwtSettings>>().Value;
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.False(options.MapInboundClaims);
        Assert.False(options.IncludeErrorDetails);
        Assert.Equal(TimeSpan.FromSeconds(30), options.TokenValidationParameters.ClockSkew);
        var now = DateTime.UtcNow;
        var key = scenario == "signature" ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(settings.SigningKeyBase64!);
        var jwt = new JwtSecurityToken(
            issuer: scenario == "issuer" ? "other-issuer" : settings.Issuer,
            audience: scenario == "audience" ? "other-audience" : settings.Audience,
            claims: [new Claim("sub", "42"), new Claim("role", "User")],
            notBefore: scenario == "future" ? now.AddMinutes(5) : now.AddHours(-1),
            expires: scenario == "missing-exp" ? null : scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(30),
            signingCredentials: scenario == "unsigned" ? null : new SigningCredentials(
                new SymmetricSecurityKey(key), scenario == "algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        if (scenario == "tampered")
        {
            var parts = token.Split('.');
            var payload = Base64UrlEncoder.Decode(parts[1]).Replace("42", "43");
            token = parts[0] + "." + Base64UrlEncoder.Encode(payload) + "." + parts[2];
        }
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token).ToString();
        var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(expected, result.Succeeded);
        if (expected)
        {
            Assert.Equal("42", result.Principal!.FindFirstValue("sub"));
            Assert.True(result.Principal!.IsInRole("User"));
        }
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("missing-key")]
    [InlineData("base64")]
    [InlineData("short-key")]
    [InlineData("repeated-key")]
    [InlineData("missing-all")]
    public void InvalidConfiguration_PreventsStartupWithoutExposingValues(string invalid)
    {
        var settings = JwtTestSettings.Create();
        switch (invalid)
        {
            case "issuer": settings.Issuer = " "; break;
            case "audience": settings.Audience = null; break;
            case "missing-key": settings.SigningKeyBase64 = null; break;
            case "base64": settings.SigningKeyBase64 = "<invalid-base64>"; break;
            case "short-key": settings.SigningKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)); break;
            case "repeated-key": settings.SigningKeyBase64 = Convert.ToBase64String(new byte[64]); break;
            case "missing-all": settings = new JwtSettings(); break;
        }
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.PostConfigure<JwtSettings>(options =>
            {
                options.Issuer = settings.Issuer;
                options.Audience = settings.Audience;
                options.SigningKeyBase64 = settings.SigningKeyBase64;
            }));
        });
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        if (!string.IsNullOrEmpty(settings.SigningKeyBase64))
            Assert.True(!error.ToString().Contains(settings.SigningKeyBase64, StringComparison.Ordinal), "El error no debe mostrar la clave.");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
