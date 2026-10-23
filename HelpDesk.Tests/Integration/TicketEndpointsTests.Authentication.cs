using HelpDesk.Api.Authentication;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests.Integration;

public partial class TicketEndpointsTests
{
    private User? regressionAdmin;

    // Cuenta persistida propia por caso y JWT real; no sustituye el esquema Bearer ni las políticas.
    private async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program>? factory = null)
    {
        regressionAdmin ??= await SeedHttpUserAsync(UserRole.Admin);
        return CreateAuthenticatedClient(regressionAdmin, factory);
    }

    private HttpClient CreateAuthenticatedClient(User user, WebApplicationFactory<Program>? factory = null)
    {
        factory ??= fixture.Factory;
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<JwtTokenIssuer>().Issue(user).AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
}
