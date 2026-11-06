using System.Data.Common;
using Npgsql;

namespace HelpDesk.Api.Demo;

public sealed class DemoTarget
{
    public const string Database = "helpdesk_demo_db";
    public const string Username = "helpdesk_demo_user";
    // Un único candado entre instancias de API y consola; nunca se usa en modo normal.
    public const long WriteLock = 10730001;
    private readonly string connectionString;
    public string InstanceId { get; }

    public DemoTarget(string? value, string? instanceId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParseExact(instanceId, "D", out var instance) || instance == Guid.Empty)
                throw Invalid();
            var raw = new DbConnectionStringBuilder { ConnectionString = value };
            string[] allowed = ["Host", "Port", "Database", "Username", "Password", "SSL Mode", "Timeout", "Command Timeout", "Pooling", "Enlist"];
            foreach (string key in raw.Keys)
                if (!allowed.Contains(key, StringComparer.OrdinalIgnoreCase)) throw Invalid();
            var settings = new NpgsqlConnectionStringBuilder(value);
            if (settings.Database != Database || settings.Username != Username ||
                string.IsNullOrWhiteSpace(settings.Password) || string.IsNullOrWhiteSpace(settings.Host) ||
                settings.Host.Contains(',') || settings.Host.Any(char.IsControl) ||
                (raw.ContainsKey("Enlist") && settings.Enlist)) throw Invalid();
            settings.Enlist = false;
            settings.SearchPath = "public";
            settings.IncludeErrorDetail = false;
            settings.PersistSecurityInfo = false;
            settings.ApplicationName = "HelpDesk.Demo";
            connectionString = settings.ConnectionString;
            InstanceId = instance.ToString("D");
        }
        catch (ArgumentException) { throw Invalid(); }
    }

    public string ConnectionString => connectionString;
    public string Marker => "HelpDesk.NET demo " + InstanceId;

    public async Task<NpgsqlConnection> OpenVerifiedAsync(CancellationToken cancellation = default)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellation);
            await using var command = new NpgsqlCommand("""
                SELECT current_database(), current_user, r.rolsuper, r.rolcreatedb, r.rolcreaterole,
                       shobj_description(d.oid, 'pg_database'), r.rolreplication, r.rolbypassrls,
                       EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = r.oid)
                FROM pg_database d JOIN pg_roles r ON r.rolname = current_user
                WHERE d.datname = current_database()
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            if (!await reader.ReadAsync(cancellation) || reader.GetString(0) != Database ||
                reader.GetString(1) != Username || reader.GetBoolean(2) || reader.GetBoolean(3) || reader.GetBoolean(4) ||
                reader.IsDBNull(5) || reader.GetString(5) != Marker ||
                reader.GetBoolean(6) || reader.GetBoolean(7) || reader.GetBoolean(8)) throw Invalid();
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("No se pudo verificar la base exclusiva de demostración. No se permite continuar.");
        }
    }

    public static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellation = default)
    {
        await using var command = new NpgsqlCommand("SET LOCAL lock_timeout = '5s'", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellation);
        command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
        command.Parameters.AddWithValue("key", WriteLock);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    private static InvalidOperationException Invalid() => new(
        "Se requiere conexión demo explícita, base helpdesk_demo_db, rol helpdesk_demo_user y UUID de instancia. No se admite la conexión habitual ni opciones adicionales.");
}
