using System.Net;
using HelpDesk.Api.Data;
using System.Net.Http.Json;
using HelpDesk.Api.Authorization;
using HelpDesk.Api.Security;
using HelpDesk.Api.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDesk.Tests;

[Collection("Rate limiting timing")]
public class RequestSecurityTests
{
    [Theory]
    [InlineData("/api/auth/login", 10)]
    [InlineData("/api/auth/register", 5)]
    [InlineData("/api/tickets", 10)]
    [InlineData("/api/tickets/1/comments", 30)]
    public async Task Limits_RejectOverflowIgnoreForwardedHeadersAndRecover(string path, int limit)
    {
        using var factory = Factory(TimeSpan.FromSeconds(2));
        using var client = factory.CreateClient();
        // Preparar EF fuera de la ventana: la primera construcción del modelo puede ser costosa.
        using (var scope = factory.Services.CreateScope())
            _ = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>().Model;
        using var warmup = await client.GetAsync("/api/health");
        for (var i = 0; i < limit; i++)
        {
            using var response = await client.PostAsync(path, new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.1");
        client.DefaultRequestHeaders.Add("Forwarded", "for=203.0.113.2");
        using var rejected = await client.PostAsync(path, new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Contains("Demasiadas", await rejected.Content.ReadAsStringAsync());
        using var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(2.2));
        using var recovered = await client.PostAsync(path, new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, recovered.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedBudget_FollowsAccountAcrossClientsAndSeparatesAccounts()
    {
        using var factory = Factory();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            using var response = await first.PostAsJsonAsync("/api/tickets", new { });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var shared = await second.PostAsJsonAsync("/api/tickets", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, shared.StatusCode);
        second.DefaultRequestHeaders.Add("X-Test-Account", "4");
        using var separate = await second.PostAsJsonAsync("/api/tickets", new { });
        Assert.Equal(HttpStatusCode.BadRequest, separate.StatusCode);
        using var comment = await first.PostAsync("/api/tickets/1/comments",
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, comment.StatusCode);
    }

    [Fact]
    public async Task AnonymousPolicies_HaveIndependentBudgetsAndReadsStayAvailable()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        for (var i = 0; i < 10; i++)
            using (var response = await client.PostAsJsonAsync("/api/auth/login", new { }))
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var register = await client.PostAsJsonAsync("/api/auth/register", new { });
        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);
        for (var i = 0; i < 35; i++)
        {
            using var read = await client.GetAsync("/api/tickets?search=" + new string('x', 201));
            Assert.Equal(HttpStatusCode.BadRequest, read.StatusCode);
        }
    }

    [Theory]
    [InlineData("title", "ok", true)]
    [InlineData("ti\0tle", "ok", false)]
    [InlineData("title", "desc\0ription", false)]
    public async Task InvalidText_Returns400BeforeSql(string title, string description, bool overflow)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/tickets", new
        { title, description = overflow ? new string('x', 10001) : description });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginAndSearch_RejectOversizeAndNullBeforeSql()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        foreach (var password in new[] { new string('x', 129), "a\0b" })
        {
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "a@example.test", password });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        foreach (var search in new[] { new string('x', 201), "a%00b" })
        {
            using var response = await client.GetAsync("/api/tickets?search=" + search);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.NotNull(UserValidation.Validate("a\0b", "a@example.test", "User", out _, out _));
        Assert.NotNull(UserValidation.Validate("a", "a\0b@example.test", "User", out _, out _));
    }

    private static WebApplicationFactory<Program> Factory(TimeSpan? window = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=unused_validation;Timeout=1" }));
            builder.ConfigureTestServices(services =>
            {
                services.AddJwtTestSettings();
                if (window is TimeSpan duration)
                    services.Replace(ServiceDescriptor.Singleton(new RequestLimits { Minute = duration, RegistrationWindow = duration }));
                services.Replace(ServiceDescriptor.Scoped<IAuthorizationService>(provider =>
                    new TestAuthorization(provider.GetRequiredService<CurrentAccount>())));
            });
        });

    private sealed class TestAuthorization(CurrentAccount account) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user,
            object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            typeof(CurrentAccount).GetProperty(nameof(CurrentAccount.Id))!.SetValue(account,
                resource is Microsoft.AspNetCore.Http.HttpContext context &&
                context.Request.Headers["X-Test-Account"] == "4" ? 4 : 3);
            return Task.FromResult(AuthorizationResult.Success());
        }
        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user,
            object? resource, string policyName) => AuthorizeAsync(user, resource, []);
    }
}

[CollectionDefinition("Rate limiting timing", DisableParallelization = true)]
public sealed class RateLimitingTimingCollection { }
