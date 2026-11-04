using System.Net;
using HelpDesk.Api.Authorization;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HelpDesk.Tests;

// Isolate parameter validation from persistence. Real JWT and database authorization
// remain covered by AuthorizationEndpointTests and the PostgreSQL integration suite.
public class AssignedToMeContractTests
{
    [Theory]
    [InlineData(UserRole.User, "true", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Admin, "true", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.User, "false", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Admin, "false", HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Technician, "", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Technician, "1", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Technician, "TRUE", HttpStatusCode.BadRequest)]
    [InlineData(UserRole.Technician, "true&assignedToMe=false", HttpStatusCode.BadRequest)]
    public async Task AssignedFilter_ValidatesBeforeDatabase(UserRole role, string value, HttpStatusCode expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Database=unused_parameter_validation" }));
            builder.ConfigureTestServices(services =>
            {
                services.AddJwtTestSettings();
                services.Replace(ServiceDescriptor.Scoped<IAuthorizationService>(provider =>
                    new TestAccountAuthorization(provider.GetRequiredService<CurrentAccount>(), role)));
            });
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/tickets?assignedToMe={value}");
        Assert.Equal(expected, response.StatusCode);
    }

    private sealed class TestAccountAuthorization(CurrentAccount account, UserRole role) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user,
            object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            // The production setters are deliberately inaccessible to application clients.
            typeof(CurrentAccount).GetProperty(nameof(CurrentAccount.Id))!.SetValue(account, 3);
            typeof(CurrentAccount).GetProperty(nameof(CurrentAccount.Role))!.SetValue(account, role);
            return Task.FromResult(AuthorizationResult.Success());
        }

        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user,
            object? resource, string policyName) => AuthorizeAsync(user, resource, []);
    }
}
