using System.Data.Common;
using HelpDesk.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelpDesk.AdminBootstrap;

// Sin ToString generado y sin exponer la cadena en mensajes de validación.
public sealed class BootstrapConnection
{
    public const string EnvironmentVariable = "HELPDESK_BOOTSTRAP_CONNECTION_STRING";
    public const string ApplicationName = "HelpDesk.AdminBootstrap";
    private readonly string connectionString;
    public string Host { get; }
    public int Port { get; }
    public string Database { get; }
    public string Username { get; }

    public BootstrapConnection(string? value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) throw Invalid();
            var raw = new DbConnectionStringBuilder { ConnectionString = value };
            string[] allowed = ["Host", "Port", "Database", "Username", "Password", "SSL Mode",
                "Timeout", "Command Timeout", "Pooling", "Include Error Detail", "Persist Security Info", "Enlist"];
            foreach (string key in raw.Keys)
                if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase)) throw Invalid();
            var settings = new NpgsqlConnectionStringBuilder(value);
            if (new[] { settings.Host, settings.Database, settings.Username, settings.Password }.Any(string.IsNullOrWhiteSpace) ||
                new[] { settings.Host!, settings.Database!, settings.Username! }.Any(text => text.Any(char.IsControl)) ||
                settings.Host!.Contains(',') || settings.Port is < 1 or > 65535 ||
                settings.IncludeErrorDetail || settings.PersistSecurityInfo || (raw.ContainsKey("Enlist") && settings.Enlist))
                throw Invalid();

            Host = settings.Host;
            Port = settings.Port;
            Database = settings.Database!;
            Username = settings.Username!;
            settings.IncludeErrorDetail = false;
            settings.PersistSecurityInfo = false;
            settings.Enlist = false;
            settings.SearchPath = "public";
            settings.ApplicationName = ApplicationName;
            connectionString = settings.ConnectionString;
        }
        catch (ArgumentException) { throw Invalid(); }
    }

    public HelpDeskDbContext CreateDbContext() => new(new DbContextOptionsBuilder<HelpDeskDbContext>()
        .UseNpgsql(connectionString).Options);

    private static InvalidOperationException Invalid() => new(
        "Configura HELPDESK_BOOTSTRAP_CONNECTION_STRING con Host, Database, Username y Password explícitos. No se admiten opciones de diagnóstico sensible, destinos múltiples ni cambios de esquema.");
}
