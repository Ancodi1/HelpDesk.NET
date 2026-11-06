using System.Security.Cryptography;
using HelpDesk.Api.Authentication;
using HelpDesk.Api.Data;
using HelpDesk.Api.Demo;
using HelpDesk.Api.Models;
using HelpDesk.Demo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Tests.Integration;

public sealed class DemoFixture : IAsyncLifetime
{
    public DemoTarget Target { get; private set; } = null!;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string UserPassword { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public string TechnicianPassword { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public string AdminPassword { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public HelpDeskDbContext Db() => new(new DbContextOptionsBuilder<HelpDeskDbContext>().UseNpgsql(Target.ConnectionString).Options);

    public async Task InitializeAsync()
    {
        Target = new DemoTarget(Environment.GetEnvironmentVariable("HELPDESK_DEMO_TEST_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("HELPDESK_DEMO_TEST_INSTANCE_ID"));
        await using var verified = await Target.OpenVerifiedAsync();
        await using (var db = Db())
        {
            // El lanzador verifica Docker/TCP y esta guardia comprueba base/rol/marca antes de migrar.
            await db.Database.MigrateAsync();
        }
        using var services = BootstrapSecurityTests.Services();
        using var scope = services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
        await new DemoRestorer(hasher).RestoreAsync(Target, "RESTORE " + DemoTarget.Database + " " + Target.InstanceId,
            true, UserPassword, TechnicianPassword, AdminPassword);
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true", ["Demo:ConnectionString"] = Environment.GetEnvironmentVariable("HELPDESK_DEMO_TEST_CONNECTION_STRING"),
                ["Demo:InstanceId"] = Target.InstanceId, ["Demo:UserPassword"] = UserPassword,
                ["Demo:TechnicianPassword"] = TechnicianPassword,
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=never_use_default"
            }));
            builder.ConfigureTestServices(services => services.AddJwtTestSettings());
        });
        using var client = Factory.CreateClient(); // Exige guardias de arranque reales.
    }

    public async Task RestoreAsync()
    {
        using var scope = Factory.Services.CreateScope();
        await new DemoRestorer(scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>())
            .RestoreAsync(Target, "RESTORE " + DemoTarget.Database + " " + Target.InstanceId, false);
    }

    public async Task<HttpClient> ClientAsync(int id)
    {
        var client = Factory.CreateClient();
        await using var db = Db();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        var token = Factory.Services.GetRequiredService<JwtTokenIssuer>().Issue(user).AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
    }
}
