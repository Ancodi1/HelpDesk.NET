using HelpDesk.Api.Authentication;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.AdminBootstrap;

public static class BootstrapProgram
{
    public static async Task<int> Main(string[] args)
    {
        // No host HTTP, configuración JWT, appsettings, User Secrets ni migraciones.
        using var services = new ServiceCollection().AddHelpDeskPasswordHashing().BuildServiceProvider();
        using var scope = services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var connection = Environment.GetEnvironmentVariable(BootstrapConnection.EnvironmentVariable);
        Environment.SetEnvironmentVariable(BootstrapConnection.EnvironmentVariable, null);
        return await new BootstrapCommand(new BootstrapConsole(), new BootstrapService(hasher)).RunAsync(args, connection);
    }
}
