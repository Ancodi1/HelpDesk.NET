using Microsoft.Extensions.Options;
using Npgsql;

namespace HelpDesk.Api.Demo;

public sealed class DemoMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<DemoSettings> options)
    {
        var settings = options.Value;
        var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
        if (!settings.Enabled || context.GetEndpoint() is null ||
            context.Request.Method is not ("POST" or "PUT" or "PATCH" or "DELETE") ||
            path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        if (path.Equals("/api/auth/register", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/users", StringComparison.OrdinalIgnoreCase) && context.Request.Method == "POST")
        {
            await Reject(context, "demo_registration_disabled", "El registro está cerrado en la demo. Utiliza las cuentas compartidas.", 403);
            return;
        }
        if (context.Request.Path.StartsWithSegments("/api/users") &&
            int.TryParse(context.Request.RouteValues["id"]?.ToString(), out var userId) && DemoAccounts.IsProtected(userId))
        {
            await Reject(context, "demo_account_protected", "Las cuentas de demostración conservan su identidad y acceso.", 409);
            return;
        }
        // Conexión de coordinación separada: los endpoints conservan sus transacciones y bloqueos.
        // Se serializan escritores demo y restauración entre procesos, sin cambiar el modo normal.
        await using var connection = await new DemoTarget(settings.ConnectionString, settings.InstanceId).OpenVerifiedAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await DemoTarget.LockAsync(connection, transaction, context.RequestAborted);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await Reject(context, "demo_busy", "La demo se está actualizando. Vuelve a intentarlo más tarde.", 409);
            return;
        }
        string? table = context.Request.Method == "POST" && path.Equals("/api/tickets", StringComparison.OrdinalIgnoreCase) ? "Tickets" :
            context.Request.Method == "POST" && context.Request.Path.StartsWithSegments("/api/tickets") && path.EndsWith("/comments", StringComparison.OrdinalIgnoreCase) ? "TicketComments" :
            context.Request.Method == "PATCH" && context.Request.Path.StartsWithSegments("/api/tickets") ? "TicketActivities" : null;
        var maximum = table == "Tickets" ? DemoSettings.MaxTickets : table == "TicketComments" ? DemoSettings.MaxComments : DemoSettings.MaxActivities;
        if (table is not null)
        {
            // Nombre de tabla de lista cerrada, nunca procede de una entrada del cliente.
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM public.\"{table}\"", connection, transaction);
            if (Convert.ToInt64(await count.ExecuteScalarAsync(context.RequestAborted)) >= maximum)
            {
                await Reject(context, "demo_capacity_reached", "La demo ha alcanzado su capacidad. Puedes seguir consultándola; necesita una restauración.", 409);
                return;
            }
        }
        await next(context);
        // Disponer la transacción libera el candado; las escrituras ya pertenecen al endpoint.
    }

    private static async Task Reject(HttpContext context, string code, string error, int status)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error, code }, context.RequestAborted);
    }
}
