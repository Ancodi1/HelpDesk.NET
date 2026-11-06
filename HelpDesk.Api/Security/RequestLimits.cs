using System.Threading.RateLimiting;
using HelpDesk.Api.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace HelpDesk.Api.Security;

// DI permite probar la recuperación con ventanas breves sin cambiar los límites reales.
public sealed record RequestLimits
{
    public TimeSpan Minute { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan RegistrationWindow { get; init; } = TimeSpan.FromMinutes(10);

    public static void Configure(RateLimiterOptions options, RequestLimits limits)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellation) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Demasiadas peticiones. Espera antes de volver a intentarlo." }, cancellation);
        };
        Add("login", 10, limits.Minute, false);
        Add("register", 5, limits.RegistrationWindow, false);
        Add("ticket-create", 10, limits.Minute, true);
        Add("comment-create", 30, limits.Minute, true);

        void Add(string name, int permits, TimeSpan window, bool account)
        {
            options.AddPolicy(name, context =>
            {
                // CurrentAccount ya ha sido validado por la autorización; nunca usar IDs enviados por el cliente.
                var key = account
                    ? "user:" + context.RequestServices.GetRequiredService<CurrentAccount>().Id
                    : "ip:" + (context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown");
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits, Window = window, QueueLimit = 0, AutoReplenishment = true
                });
            });
        }
    }
}
