using System.Data.Common;
using Npgsql;

namespace HelpDesk.Tests.Integration;

public static class PostgreSqlTestSettings
{
    public const string EnvironmentVariable = "HELPDESK_TEST_CONNECTION_STRING";
    public const string DatabaseName = "helpdesk_test_db";
    public const string UserName = "helpdesk_test_user";

    public static string Validate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Configura {EnvironmentVariable} fuera del repositorio para ejecutar las pruebas PostgreSQL.");
        }

        // Lista cerrada: impide opciones que alteren el destino o el esquema de las migraciones.
        string[] allowedKeys = ["Host", "Port", "Database", "Username", "Password", "SSL Mode", "Timeout", "Command Timeout", "Pooling",
            "Include Error Detail", "Persist Security Info", "Enlist"];
        DbConnectionStringBuilder raw;
        try
        {
            raw = new DbConnectionStringBuilder { ConnectionString = connectionString };
        }
        catch (ArgumentException)
        {
            // No incluir la excepción original: podría contener fragmentos de configuración.
            throw new InvalidOperationException("Sintaxis no válida en la cadena PostgreSQL de pruebas. Usa un constructor de cadenas para citar valores especiales.");
        }

        foreach (string key in raw.Keys)
        {
            if (!allowedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("La cadena PostgreSQL de pruebas contiene una opción no permitida.");
            }
        }

        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("La cadena PostgreSQL de pruebas contiene un valor de opción no válido.");
        }

        if (settings.Database != DatabaseName || settings.Username != UserName ||
            settings.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            string.IsNullOrWhiteSpace(settings.Password) || settings.Port is < 1 or > 65535 ||
            settings.IncludeErrorDetail || settings.PersistSecurityInfo ||
            (raw.ContainsKey("Enlist") && settings.Enlist))
        {
            throw new InvalidOperationException("Configuración de seguridad incorrecta: se requieren PostgreSQL local, Database=helpdesk_test_db, Username=helpdesk_test_user y contraseña explícita. Include Error Detail, Persist Security Info y Enlist solo admiten false. No se ha abierto ninguna conexión.");
        }

        settings.IncludeErrorDetail = false;
        settings.PersistSecurityInfo = false;
        settings.Enlist = false;
        return settings.ConnectionString;
    }
}
