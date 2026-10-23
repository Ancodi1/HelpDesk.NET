using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using HelpDesk.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Tests;

public class AuthorizationEndpointTests
{
    public static IEnumerable<object[]> ProtectedRequests()
    {
        var routes = new[]
        {
            ("GET", "/api/tickets"), ("GET", "/api/tickets/1"), ("POST", "/api/tickets"),
            ("PUT", "/api/tickets/1"), ("DELETE", "/api/tickets/1"),
            ("PATCH", "/api/tickets/1/status"), ("PATCH", "/api/tickets/1/priority"),
            ("PATCH", "/api/tickets/1/category"), ("PATCH", "/api/tickets/1/assignment"),
            ("GET", "/api/users"), ("GET", "/api/users/1"), ("POST", "/api/users"), ("PUT", "/api/users/1")
        };
        foreach (var (method, route) in routes)
            foreach (var token in new[] { "missing", "invalid", "expired", "tampered" })
                yield return [method, route, token];
    }

    [Theory]
    [MemberData(nameof(ProtectedRequests))]
    public async Task ProtectedEndpoint_RejectsMissingOrInvalidTokenBeforeBodyAndDatabase(string method, string route, string scenario)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        // La autorización precede a la deserialización incluso para un cuerpo incompatible.
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = new StringContent("{", Encoding.UTF8, "application/json");
        if (scenario != "missing")
        {
            var settings = factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
            var token = scenario == "invalid" ? "invalid-token" : new JwtSecurityTokenHandler().WriteToken(
                new JwtSecurityToken(settings.Issuer, settings.Audience,
                    [new Claim("sub", "1"), new Claim("role", "Admin")],
                    expires: scenario == "expired" ? DateTime.UtcNow.AddMinutes(-2) : DateTime.UtcNow.AddMinutes(30),
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(
                        Convert.FromBase64String(settings.SigningKeyBase64!)), SecurityAlgorithms.HmacSha256)));
            if (scenario == "tampered")
            {
                var parts = token.Split('.');
                parts[1] = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(parts[1]).Replace("Admin", "User"));
                token = string.Join('.', parts);
            }
            request.Headers.Authorization = new("Bearer", token);
        }
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void OnlyHealthRegistrationAndLogin_AreAnonymous_AndFallbackRequiresCurrentAccount()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.Cast<RouteEndpoint>().ToArray();
        var anonymous = routes.Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText).OrderBy(path => path);
        Assert.Equal(new[] { "/api/auth/login", "/api/auth/register", "/api/health" }, anonymous);
        Assert.Equal(16, routes.Length);
        Assert.All(routes.Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null),
            endpoint => Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
        var options = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.Same(options.DefaultPolicy, options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy!.Requirements,
            requirement => requirement is HelpDesk.Api.Authorization.CurrentAccountRequirement);
    }

    [Theory]
    [InlineData("missing-sub")]
    [InlineData("duplicate-sub")]
    [InlineData("zero")]
    [InlineData("negative")]
    [InlineData("non-numeric")]
    [InlineData("missing-role")]
    [InlineData("duplicate-role")]
    [InlineData("unknown-role")]
    public async Task SignedToken_WithAmbiguousOrInvalidIdentity_IsForbiddenBeforeDatabase(string scenario)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var settings = factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var claims = new List<Claim>();
        if (scenario != "missing-sub")
            claims.Add(new Claim("sub", scenario switch { "zero" => "0", "negative" => "-1", "non-numeric" => "invalid", _ => "1" }));
        if (scenario == "duplicate-sub") claims.Add(new Claim("sub", "2"));
        if (scenario != "missing-role") claims.Add(new Claim("role", scenario == "unknown-role" ? "2" : "Admin"));
        if (scenario == "duplicate-role") claims.Add(new Claim("role", "User"));
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(settings.Issuer, settings.Audience,
            claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKeyBase64!)), SecurityAlgorithms.HmacSha256)));
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await client.GetAsync("/api/users");
        // El lector JWT rechaza sub como array antes de llegar a autorización.
        Assert.Equal(scenario == "duplicate-sub" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    internal static WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddJwtTestSettings());
        });
}
