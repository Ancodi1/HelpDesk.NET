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
            services.RemoveAll<HelpDeskDbContext>();
            services.RemoveAll<DbContextOptions<HelpDeskDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<HelpDeskDbContext>>();
            services.AddDbContext<HelpDeskDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
}
