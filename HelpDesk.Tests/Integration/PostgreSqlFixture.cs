using HelpDesk.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HelpDesk.Tests.Integration;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public TicketApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = PostgreSqlTestSettings.Validate(
            Environment.GetEnvironmentVariable(PostgreSqlTestSettings.EnvironmentVariable));

        try
        {
            // Abrir directamente la base exigida. Si no existe, fallar: nunca crearla.
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT current_database(), current_user, rolsuper, rolcreatedb, rolcreaterole
                FROM pg_roles WHERE rolname = current_user
                """, connection);
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync() ||
                    reader.GetString(0) != PostgreSqlTestSettings.DatabaseName ||
                    reader.GetString(1) != PostgreSqlTestSettings.UserName ||
                    reader.GetBoolean(2) || reader.GetBoolean(3) || reader.GetBoolean(4))
                {
                    throw new InvalidOperationException("Destino o permisos de pruebas inseguros. No se aplicaron migraciones.");
                }
            }

            // La conexión anterior confirma existencia e identidad antes de MigrateAsync.
            Factory = new TicketApiFactory(connectionString);
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
            await db.Database.MigrateAsync();
        }
        catch (NpgsqlException)
        {
            throw new InvalidOperationException("No se pudo verificar o preparar helpdesk_test_db. Revisa su existencia, permisos y configuración fuera del repositorio. No se crean bases automáticamente.");
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}
