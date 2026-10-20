using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HelpDesk.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_ReturnsOkWithExpectedJson()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var property = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("status", property.Name);
        Assert.Equal("ok", property.Value.GetString());
    }
}
