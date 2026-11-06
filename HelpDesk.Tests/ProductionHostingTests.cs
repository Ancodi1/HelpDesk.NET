using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace HelpDesk.Tests;

public class ProductionHostingTests
{
    [Theory]
    [InlineData("/api/health/live", HttpStatusCode.OK)]
    [InlineData("/api/health/ready", HttpStatusCode.ServiceUnavailable)]
    [InlineData("/api/unknown/deep", HttpStatusCode.NotFound)]
    public async Task ProbesAndUnknownApi_AreSafeWithoutDatabase(string path, HttpStatusCode status)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddJwtTestSettings());
        });
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ConnectionStrings", body);
        Assert.DoesNotContain("Exception", body);
        if (status == HttpStatusCode.ServiceUnavailable)
            Assert.Equal("{\"status\":\"unavailable\"}", body);
    }
}
