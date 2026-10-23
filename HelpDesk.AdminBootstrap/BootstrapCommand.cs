using HelpDesk.Api.Data;

namespace HelpDesk.AdminBootstrap;

public sealed class BootstrapCommand(IBootstrapConsole console, BootstrapService service)
{
    public async Task<int> RunAsync(string[] args, string? connectionString,
        CancellationToken cancellationToken = default)
    {
        if (args is ["--help"])
        {
            console.WriteLine("Uso: dotnet run --project HelpDesk.AdminBootstrap (terminal interactiva). Configura HELPDESK_BOOTSTRAP_CONNECTION_STRING fuera del repositorio. No se aceptan credenciales por argumentos.");
            return 0;
        }
        if (args.Length != 0 || !console.IsInteractive)
        {
            console.WriteLine("Se requiere terminal interactiva y ningún argumento. No se aceptan contraseñas por argumentos ni entrada redirigida.");
            return 2;
        }
        try
        {
            var target = new BootstrapConnection(connectionString);
            console.WriteLine($"Host: {target.Host}; puerto: {target.Port}; base: {target.Database}; usuario PostgreSQL: {target.Username}.");
            console.WriteLine("Operación: crear un Admin NUEVO, únicamente si no existe ningún Admin. No se activarán ni modificarán cuentas existentes.");
            var database = console.ReadLine("Escribe el nombre exacto de la base de destino: ");
            if (database != target.Database)
            {
                console.WriteLine("Cancelado: la base indicada no coincide con el destino.");
                return 2;
            }
            var email = console.ReadLine("Email de la cuenta nueva: ");
            var name = console.ReadLine("Nombre: ");
            var password = console.ReadSecret("Contraseña (oculta): ");
            if (password is null) return Cancel();
            var repeated = console.ReadSecret("Repite la contraseña (oculta): ");
            if (repeated is null) return Cancel();
            var error = BootstrapService.Validate(name, email, password, repeated);
            if (error is not null)
            {
                console.WriteLine(error);
                return 2;
            }
            var confirmation = console.ReadLine($"Escribe exactamente {BootstrapService.Confirmation}: ");
            if (confirmation != BootstrapService.Confirmation) return Cancel();

            await using HelpDeskDbContext db = target.CreateDbContext();
            var result = await service.CreateAsync(db, target, database, confirmation, name, email, password, repeated, cancellationToken);
            console.WriteLine(result switch
            {
                BootstrapResult.Created => "Primer Admin creado. Inicia sesión mediante /api/auth/login. No se han modificado cuentas ni tickets anteriores.",
                BootstrapResult.AdminExists => "Rechazado: ya existe un Admin, aunque no tenga contraseña. No se ha modificado ninguna cuenta.",
                BootstrapResult.EmailExists => "Rechazado: el correo ya existe. No se ha promovido ni modificado esa cuenta.",
                BootstrapResult.WrongTarget => "Cancelado: la identidad de la conexión no coincide con el destino confirmado.",
                _ => "Operación cancelada sin crear una cuenta."
            });
            return result == BootstrapResult.Created ? 0 : 2;
        }
        catch (OperationCanceledException) { return Cancel(); }
        catch (Exception)
        {
            // No volcar excepciones del proveedor, configuración o datos introducidos.
            console.WriteLine("No se pudo completar el bootstrap. Comprueba conexión, permisos y esquema existente sin compartir secretos. Si se perdió la conexión al confirmar, verifica el resultado antes de reintentar.");
            return 1;
        }
    }

    private int Cancel()
    {
        console.WriteLine("Operación cancelada sin crear una cuenta.");
        return 2;
    }
}
