using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace HelpDesk.Api.Authentication;

public static class PasswordHashing
{
    // Registro HTTP y CLI comparten parámetros y la implementación del framework.
    public static IServiceCollection AddHelpDeskPasswordHashing(this IServiceCollection services)
    {
        services.Configure<PasswordHasherOptions>(options =>
        {
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = 210_000;
        });
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        return services;
    }
}
