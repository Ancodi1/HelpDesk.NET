using Microsoft.AspNetCore.Builder;
using HelpDesk.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace HelpDesk.Tests.Integration;

public sealed class TicketApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;

    public TicketApiFactory(string connectionString)
    {
        this.connectionString = PostgreSqlTestSettings.Validate(connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // No volcar detalles del proveedor ni parámetros en las salidas de los tests.
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddJwtTestSettings();
            services.AddSingleton<IStartupFilter, ClientAddressFilter>();
            services.RemoveAll<HelpDeskDbContext>();
            services.RemoveAll<DbContextOptions<HelpDeskDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HelpDeskDbContext>>();
            services.AddDbContext<HelpDeskDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Test-Client", Guid.NewGuid().ToString("N"));
    }

    // TestServer carece de conexiones TCP distintas: simular una dirección por cliente
    // conserva los límites reales sin que casos independientes consuman el mismo cupo.
    private sealed class ClientAddressFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (Guid.TryParse(context.Request.Headers["X-Test-Client"], out var clientId))
                    context.Connection.RemoteIpAddress = new System.Net.IPAddress(clientId.ToByteArray());
                await continuation();
            });
            next(app);
        };
    }

}
