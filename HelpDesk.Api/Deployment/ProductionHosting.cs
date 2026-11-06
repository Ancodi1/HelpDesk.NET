using System.Net;
using HelpDesk.Api.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Deployment;

public static class ProductionHosting
{
    public static void Configure(WebApplicationBuilder builder)
    {
        // Secret files are mounted at runtime, never copied into the image.
        builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
        if (builder.Environment.IsProduction())
        {
            var hosts = builder.Configuration["AllowedHosts"];
            if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';').Any(h => h.Contains('*') || string.IsNullOrWhiteSpace(h)))
                throw new InvalidOperationException("Producción requiere AllowedHosts explícitos, sin comodines.");
            if (builder.Configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED"))
                throw new InvalidOperationException("Configura proxies explícitos mediante ReverseProxy:KnownProxies.");
        }
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
    }

    public static void UseProductionHosting(this WebApplication app)
    {
        var proxies = app.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        if (proxies.Length > 0)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = 1,
                RequireHeaderSymmetry = true
            };
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
            app.UseForwardedHeaders(options);
        }
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                // Angular inserts component styles dynamically; avoid an unvalidated CSP.
                headers["Content-Security-Policy"] = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
                headers.CacheControl = "no-store";
                if (app.Environment.IsProduction() && context.Request.IsHttps)
                    headers["Strict-Transport-Security"] = "max-age=31536000";
                return Task.CompletedTask;
            });
            await next(context);
        });
        // /api is reserved, including when a matching file exists on disk.
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"), branch =>
        {
            branch.UseDefaultFiles();
            branch.UseStaticFiles();
        });
    }

    public static void MapProductionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        app.MapGet("/api/health/ready", async (IServiceProvider services, CancellationToken cancellation) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                var db = services.GetRequiredService<HelpDeskDbContext>();
                // Query required tables as well as the connection: a missing schema is not ready.
                await db.Users.Select(u => u.Id).Take(1).ToArrayAsync(timeout.Token);
                await db.Tickets.Select(t => t.Id).Take(1).ToArrayAsync(timeout.Token);
                await db.TicketComments.Select(t => t.Id).Take(1).ToArrayAsync(timeout.Token);
                await db.TicketActivities.Select(t => t.Id).Take(1).ToArrayAsync(timeout.Token);
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception) { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
        }).AllowAnonymous();
        app.MapMethods("/api/{**path}", ["GET", "HEAD"], () => Results.NotFound()).AllowAnonymous();
        if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
            app.MapFallbackToFile("index.html").AllowAnonymous();
    }
}
