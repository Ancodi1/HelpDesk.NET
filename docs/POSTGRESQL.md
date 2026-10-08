# PostgreSQL y EF Core — preparación local

Los dos GET ya consultan PostgreSQL mediante EF Core; POST, PUT y DELETE siguen utilizando `List<Ticket>`.
PostgreSQL 16 está funcionando y la base `helpdesk_db` y el usuario `helpdesk_user` ya existen según la configuración comunicada por el desarrollador. La herramienta local `dotnet-ef` 10.0.12 está instalada y `InitialCreate` fue aplicada por el desarrollador antes de migrar los GET. No se han creado tablas ni modificado datos desde esta tarea.

## Versiones

- Proyecto: `net10.0`; SDK comprobado: `10.0.112`.
- `Microsoft.EntityFrameworkCore` y `Microsoft.EntityFrameworkCore.Design`: `10.0.12`.
- `Npgsql.EntityFrameworkCore.PostgreSQL`: `10.0.3`, compatible con EF Core `>= 10.0.4` y `< 11.0.0`.
- El paquete Design prepara las herramientas de migración y tiene `PrivateAssets=all` para no propagarse a proyectos consumidores.

Las referencias están en el `.csproj`. Para reproducir su incorporación desde la raíz:

```bash
dotnet add HelpDesk.Api/HelpDesk.Api.csproj package Microsoft.EntityFrameworkCore --version 10.0.12
dotnet add HelpDesk.Api/HelpDesk.Api.csproj package Microsoft.EntityFrameworkCore.Design --version 10.0.12
dotnet add HelpDesk.Api/HelpDesk.Api.csproj package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.3
dotnet build HelpDesk.NET.slnx
```

## Instalar PostgreSQL en Linux Mint 22.3

Mint 22.3 utiliza la base Ubuntu 24.04 (`noble`). Se utiliza el paquete de la distribución, sin añadir repositorios externos. La versión principal prevista en esa base es PostgreSQL 16; `apt policy` muestra la versión disponible en el equipo.

```bash
cat /etc/os-release
sudo apt update
apt policy postgresql
sudo apt install postgresql postgresql-client
sudo systemctl enable --now postgresql
pg_lsclusters
pg_isready -h localhost -p 5432
psql --version
```

`pg_lsclusters` permite comprobar el puerto real del clúster. Estos comandos son instrucciones para ejecutar localmente, no comprobaciones ya realizadas por esta tarea.

## Crear usuario y base local, sin tablas

La base y el usuario ya están creados; los siguientes comandos son una referencia para preparar otro entorno, no deben repetirse en el actual. Abrir la consola administrativa:

```bash
sudo -u postgres psql
```

Dentro de `psql`, ejecutar una sola vez:

```sql
CREATE ROLE helpdesk_user LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
\password helpdesk_user
CREATE DATABASE helpdesk_db OWNER helpdesk_user;
\q
```

`\password` solicita la contraseña sin escribirla en un comando SQL ni en el historial. El usuario es propietario únicamente de la base de desarrollo, para poder aplicar las futuras migraciones; no es superusuario. No se crea ninguna tabla en este paso.

Probar la conexión por TCP, con contraseña solicitada interactivamente:

```bash
psql -h localhost -p 5432 -U helpdesk_user -d helpdesk_db -W -c 'SELECT current_database();'
```

Si el puerto difiere, usar el indicado por `pg_lsclusters` también en la cadena de conexión. No cambiar la autenticación a `trust` para evitar introducir una contraseña.

## Cadena de conexión fuera de Git

El proyecto ya contiene `UserSecretsId`; no hay que ejecutar `user-secrets init` otra vez. ASP.NET Core carga User Secrets automáticamente en el entorno `Development`. El identificador del almacén no es una credencial y se puede versionar. No guardar valores reales en `appsettings*.json`, archivos de peticiones ni documentación.

La clave utilizada es `ConnectionStrings:DefaultConnection`. Formato ilustrativo, con marcador sin contraseña real:

```text
Host=localhost;Port=5432;Database=helpdesk_db;Username=helpdesk_user;Password=<CONTRASEÑA_LOCAL>
```

Desde la raíz del repositorio, introducir la cadena completa mediante una entrada oculta y enviarla a Secret Manager por stdin, sin ponerla en argumentos o historial:

```bash
python3 - <<'PY'
import getpass
import json
import subprocess

connection = getpass.getpass('Cadena de conexión local completa (entrada oculta): ')
if not connection.strip():
    raise SystemExit('No se ha guardado una cadena vacía.')
result = subprocess.run(
    ['dotnet', 'user-secrets', 'set', '--project', 'HelpDesk.Api/HelpDesk.Api.csproj'],
    input=json.dumps({'ConnectionStrings:DefaultConnection': connection}),
    text=True,
    capture_output=True,
)
if result.returncode:
    raise SystemExit('No se pudo guardar la configuración local; no se muestra su contenido.')
print('Configuración local guardada sin mostrar su valor.')
PY
```

Si la contraseña incluye caracteres especiales de una cadena Npgsql, delimitar su valor con comillas dobles y duplicar las comillas dobles internas. No compartir la salida de `dotnet user-secrets list`, porque muestra los valores.

User Secrets guarda datos fuera del repositorio, en el perfil del usuario, pero **no los cifra**: es una herramienta de desarrollo. Como alternativa, la configuración estándar permite la variable de entorno `ConnectionStrings__DefaultConnection`; suministrarla desde un mecanismo local que no la registre ni la imprima.

Arranque con secretos de desarrollo:

```bash
dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-launch-profile --urls http://127.0.0.1:5080 -- --environment Development
```

La compilación no necesita credenciales. Los GET solicitan el contexto y necesitan la cadena de conexión y la tabla existente. Al resolver `HelpDeskDbContext`, se comprueba que existe la cadena y se configura Npgsql; si falta, se lanza un error sin incluir valores sensibles. Configurar el contexto no abre por sí solo una conexión ni crea el esquema.

## Modelo preparado; persistencia pendiente

`HelpDeskDbContext` expone `DbSet<Ticket> Tickets` y configura:

- `Id`: clave primaria con generación PostgreSQL `identity by default`, respaldada por una secuencia. Los IDs pueden tener huecos; no se garantiza una numeración consecutiva.
- `Title`: columna obligatoria, longitud máxima 100.
- `Description`: columna obligatoria.
- `CreatedAt`: `timestamp with time zone`; Npgsql requiere `DateTime` UTC para escribir este tipo. El POST actual ya usa `DateTime.UtcNow`.

`IsRequired` impide NULL en el esquema futuro; no rechaza espacios ni sustituye la validación HTTP. Esas reglas siguen en `ValidateTicket`. La clase `Ticket` conserva sus propiedades y no necesita atributos adicionales.

`InitialCreate` ya describe el esquema en `HelpDesk.Api/Migrations`; aplicarla creará la tabla. El desarrollador ya aplicó esta migración. No volver a crear o aplicar esquemas desde el arranque con `EnsureCreated` o `Migrate`. La sustitución de la lista en las escrituras por `SaveChangesAsync` requiere una etapa solicitada.

## Herramienta local y primera migración

`dotnet-tools.json` fija `dotnet-ef` 10.0.12. En un nuevo checkout, restaurar la herramienta:

```bash
dotnet tool restore
dotnet ef --version
```

Comando utilizado para generar la migración (ya ejecutado; no repetir):

```bash
dotnet ef migrations add InitialCreate --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext --output-dir Migrations -- --environment Development
```

La opción de entorno permite cargar User Secrets bajo `ConnectionStrings:DefaultConnection`. No se ha impreso ni modificado su contenido. EF puede crear el contexto desde DI sin una fábrica adicional. Generar la migración no aplica SQL a PostgreSQL.

Para verificar los archivos sin conectar a la base de datos:

```bash
dotnet build HelpDesk.NET.slnx
dotnet ef migrations list --no-connect --no-build --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext -- --environment Development
```

Los archivos generados tienen tres responsabilidades: `InitialCreate.cs` contiene `Up` (crear `Tickets`) y `Down` (eliminarla al revertir); su `.Designer.cs` contiene los metadatos y el modelo objetivo; `HelpDeskDbContextModelSnapshot.cs` conserva la instantánea para calcular futuras migraciones. Ninguno contiene credenciales ni datos de los tickets de ejemplo.

## Fuentes oficiales

- [Instalación de PostgreSQL en Ubuntu](https://www.postgresql.org/download/linux/ubuntu/).
- [Paquete PostgreSQL 16 de Ubuntu noble](https://packages.ubuntu.com/noble/postgresql-16).
- [Npgsql EF Core y dependencias de su versión](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3).
- [EF Core 10.0.12](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.12).
- [DbContext e inyección de dependencias](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/).
- [Secret Manager](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).
- [Migraciones](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/).
- [IDs generados con Npgsql](https://www.npgsql.org/efcore/modeling/generated-properties.html).
- [Fechas UTC con Npgsql](https://www.npgsql.org/doc/types/datetime.html).
