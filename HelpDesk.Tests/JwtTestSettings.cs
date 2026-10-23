using System.Security.Cryptography;
using HelpDesk.Api.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Tests;

internal static class JwtTestSettings
{
    public static JwtSettings Create() => new()
    {
        Issuer = "helpdesk-tests", Audience = "helpdesk-test-client",
        SigningKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
    };

    public static void AddJwtTestSettings(this IServiceCollection services)
    {
        var settings = Create();
        services.PostConfigure<JwtSettings>(options =>
        {
            options.Issuer = settings.Issuer;
            options.Audience = settings.Audience;
            options.SigningKeyBase64 = settings.SigningKeyBase64;
        });
    }
}
