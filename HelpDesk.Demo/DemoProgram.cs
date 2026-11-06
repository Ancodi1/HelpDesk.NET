using HelpDesk.Api.Authentication;
using HelpDesk.Api.Demo;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.Demo;

public static class DemoProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Uso: --check | --initialize/--restore --confirm <UUID>. Solo HELPDESK_DEMO_CONNECTION_STRING e HELPDESK_DEMO_INSTANCE_ID. Inicialización: HELPDESK_DEMO_USER_PASSWORD, HELPDESK_DEMO_TECHNICIAN_PASSWORD y HELPDESK_DEMO_ADMIN_PASSWORD. Nunca pasar secretos por argumentos.");
            return 0;
        }
        if (args is not ["--check"] &&
            !(args is ["--initialize" or "--restore", "--confirm", _]))
        {
            Console.WriteLine("Argumentos no válidos. Consulta --help.");
            return 2;
        }
        try
        {
            string? Take(string name)
            {
                var value = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
                return value;
            }
            var target = new DemoTarget(Take("HELPDESK_DEMO_CONNECTION_STRING"), Take("HELPDESK_DEMO_INSTANCE_ID"));
            if (args is ["--check"])
            {
                await using var connection = await target.OpenVerifiedAsync();
                Console.WriteLine("Identidad demo verificada. No se ha modificado ningún dato.");
                return 0;
            }
            using var services = new ServiceCollection().AddHelpDeskPasswordHashing().BuildServiceProvider();
            using var scope = services.CreateScope();
            var restorer = new DemoRestorer(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
            await restorer.RestoreAsync(target, "RESTORE " + DemoTarget.Database + " " + args[2], args[0] == "--initialize",
                Take("HELPDESK_DEMO_USER_PASSWORD"), Take("HELPDESK_DEMO_TECHNICIAN_PASSWORD"), Take("HELPDESK_DEMO_ADMIN_PASSWORD"));
            Console.WriteLine("Datos ficticios preparados. No se han impreso credenciales ni modificado el esquema.");
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine("Operación rechazada o fallida. Comprueba identidad, confirmación, permisos y esquema demo. No compartas configuración sensible. Ante pérdida de conexión en commit, verifica el resultado antes de reintentar.");
            return 1;
        }
    }
}
