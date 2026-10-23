# HelpDesk.NET

Aplicación de gestión de incidencias técnicas: permite registrar tickets, actualizar su información y clasificarlos por estado, prioridad y categoría.

Es un **proyecto personal en desarrollo**, con un enfoque educativo y de portfolio. Su objetivo es practicar el diseño de APIs, la persistencia relacional y las pruebas automatizadas mediante una evolución incremental. Actualmente ofrece una API HTTP; la interfaz web y la autenticación están previstas para etapas posteriores.

[Repositorio en GitHub](https://github.com/Ancodi1/HelpDeskt.NET)

## Tecnologías

| Tecnología | Uso |
| --- | --- |
| C# y .NET 10 | Lenguaje y plataforma del backend |
| ASP.NET Core Minimal APIs | Endpoints HTTP y serialización JSON |
| Entity Framework Core 10 | Acceso a datos y migraciones |
| PostgreSQL y Npgsql | Persistencia relacional |
| xUnit | Pruebas unitarias y de integración |
| WebApplicationFactory / TestServer | Pruebas HTTP en memoria |

## Funcionalidades

- CRUD de tickets con persistencia en PostgreSQL.
- Consulta de usuarios y registro público de cuentas con contraseña y rol `User`.
- Autorización por roles y recursos: tickets propios/asignados y administración exclusiva de Admin.
- Creador opcional y asignación/reasignación/desasignación de técnicos en tickets.
- Listado con filtros combinables, búsqueda sin distinguir mayúsculas y paginación.
- Validación de título obligatorio, máximo de 100 caracteres, y descripción obligatoria.
- Estados con transiciones controladas y respuesta `409 Conflict` cuando el cambio no está permitido.
- Prioridades: `Low`, `Medium`, `High` y `Critical`.
- Categorías: `Hardware`, `Software`, `Network`, `Access` y `Other`.
- Actualizaciones independientes de estado, prioridad y categoría. Repetir el valor actual devuelve `200` sin una escritura innecesaria.
- Enums como texto en PostgreSQL y JSON; fechas de creación en UTC.
- Errores de validación en JSON y respuestas breves para cuerpos JSON incorrectos.

| Estado actual | Estados de destino permitidos |
| --- | --- |
| `Open` | `InProgress` |
| `InProgress` | `Resolved`, `Open` |
| `Resolved` | `Closed`, `InProgress`, `Open` |
| `Closed` | `Open` |

## Estructura

```text
HelpDesk.NET.slnx
HelpDesk.Api/
  Program.cs          # Arranque, configuración y endpoints
  Models/             # Ticket, enums y reglas de negocio
  DTOs/               # Contratos de entrada y respuesta
  Validation/         # Validación compartida de solicitudes de usuarios
  Data/               # DbContext y mapeo de entidades
  Migrations/         # Evolución del esquema PostgreSQL
HelpDesk.Tests/
  Integration/        # Fábrica HTTP, fixture y pruebas PostgreSQL
  ...                 # Pruebas unitarias y del endpoint de salud
  HelpDesk.Tests.csproj
docs/                 # Arquitectura, progreso y guías de configuración
dotnet-tools.json     # Herramienta local dotnet-ef
```

La solución mantiene una estructura sencilla: los endpoints utilizan directamente el contexto de EF Core y las reglas de transición se expresan en una función independiente.

## Usuarios y roles

`User` conserva Id, Name (máximo 100), Email (máximo 254), Role y CreatedAt UTC. Añade `PasswordHash` nullable: los usuarios anteriores conservan null y sus relaciones con tickets. No se generan contraseñas ficticias. Los DTOs públicos solo contienen `id`, `name`, `email`, `role`, `createdAt`; el hash lleva además `JsonIgnore` en la entidad.

`POST /api/auth/register` recibe `name`, `email` y `password`. Reutiliza la validación de nombre/correo, normaliza Email con `Trim().ToLowerInvariant()` y crea exclusivamente `User`; cualquier campo adicional `role`, `passwordHash`, `id` o `createdAt` se ignora. Devuelve 201 con DTO público y Location `/api/users/{id}`, 400 por entrada inválida o 409 por correo duplicado. El índice único protege también registros concurrentes. No reemplaza contraseñas de cuentas anteriores.

La contraseña exige 15–128 unidades UTF-16, no puede ser solo espacios ni contener caracteres de control. Permite Unicode y espacios; no se recorta, normaliza ni trunca. Se usa `PasswordHasher<User>` V3 (PBKDF2-HMAC-SHA512, 210.000 iteraciones y salt aleatorio), sin instalar Identity completo. Hay login con JWT firmado HS256 de 30 minutos; hay autorización por roles y recursos, sin recuperación ni envío de correos. Ver [configuración y prueba local de autenticación](docs/AUTHENTICATION.md).

Email conserva la política anterior: formato mediante `MailAddress.TryCreate`, sin nombres de presentación ni espacios internos, dominio ASCII con al menos un punto; dominios internacionalizados en punycode. PostgreSQL exige correo canónico mediante CHECK e índice único. Los GET proyectan DTOs con AsNoTracking y el listado sigue siendo un array ordenado por Id.

**Etapa 9.3:** solo health, registro y login son públicos. Toda otra ruta requiere JWT válido y una cuenta existente con el mismo rol actual en PostgreSQL. POST/PUT de usuarios se habilitan solo para Admin; no permiten fijar contraseñas/hashes. Las cuentas administrativas creadas así tienen PasswordHash NULL y requieren un futuro flujo seguro de activación. No existe bootstrap de Admin ni se modifican cuentas existentes para obtenerlo. Perfil propio disponible en GET `/api/users/{id}`; directorio completo reservado a Admin.

## API

URL del perfil HTTP local: `http://localhost:5085`. Salvo health, registro y login, enviar `Authorization: Bearer <TOKEN_LOCAL>`; los ejemplos de gestión requieren Admin. No ejecutar escrituras de ejemplo sobre datos existentes.

| Método | Ruta | Comportamiento |
| --- | --- | --- |
| GET | `/api/users` | Solo Admin; `200` con DTOs ordenados por Id |
| GET | `/api/users/{id}` | Admin o perfil propio; `200` con DTO o `404` |
| POST | `/api/auth/login` | `200` con accessToken, tokenType Bearer y expiresIn 1800; `400` o `401` genérico |
| POST | `/api/auth/register` | `201` con DTO y Location, `400` o `409`; siempre rol User |
| POST | `/api/users` | Solo Admin; `201`, `400` o `409`; sin credencial |
| PUT | `/api/users/{id}` | Solo Admin; `200`, `400`, `404` o `409` |
| GET | `/api/health` | `200` con `{"status":"ok"}` |
| GET | `/api/tickets` | `200` con `items` y metadatos de paginación |
| GET | `/api/tickets/{id}` | `200` con el ticket, o `404` |
| POST | `/api/tickets` | `201` con ticket y Location, `400` o `409` por conflicto de referencia concurrente |
| PUT | `/api/tickets/{id}` | Actualiza título y descripción; `200`, `400` o `404` |
| PATCH | `/api/tickets/{id}/status` | Cambia el estado; `200`, `400`, `404` o `409` |
| PATCH | `/api/tickets/{id}/priority` | Cambia la prioridad; `200`, `400` o `404` |
| PATCH | `/api/tickets/{id}/category` | Cambia la categoría; `200`, `400` o `404` |
| PATCH | `/api/tickets/{id}/assignment` | Asigna, reasigna o desasigna; `200`, `400`, `404` o `409` |
| DELETE | `/api/tickets/{id}` | `204` sin cuerpo al eliminar, o `404` |

### Permisos de tickets

| Operación | User | Technician | Admin |
| --- | --- | --- | --- |
| Listado y detalle | Creados por él | Creados por él o asignados a él | Todos |
| Crear | Propio | Propio | Propio |
| Cambiar estado | No | Solo asignados a él | Todos |
| PUT, prioridad, categoría, asignación y DELETE | No | No | Todos |

Los recuentos y filtros incluyen solo tickets visibles. El detalle ajeno devuelve 404. Las operaciones vetadas por rol devuelven 403; ausencia de JWT válido, 401 con challenge Bearer. Un técnico puede leer un ticket propio no asignado pero no cambiar su estado (403). Tickets históricos con creador NULL: solo Admin o técnico asignado. En POST se ignora `createdByUserId` del cuerpo y se toma siempre el ID del JWT validado y contrastado con PostgreSQL, también para Admin; la creación no asigna técnico.

### Comprobación manual de usuarios

Los GET de usuarios siguen disponibles. Para registrar, enviar `name`, `email` y una contraseña nueva elegida localmente a `/api/auth/register`. Introducirla desde un cliente que no guarde cuerpos sensibles en historial, logs ni capturas compartidas; no colocar contraseñas reales en comandos versionados. Se documenta el contrato y la validación en [AUTHENTICATION.md](docs/AUTHENTICATION.md).

POST/PUT de usuarios requieren Admin y conservan validaciones de campos, unicidad y restricciones de asignación. La [guía de pruebas manuales por rol](docs/AUTHENTICATION.md#prueba-manual-de-cada-rol-en-linux-mint-sin-escribir-datos) usa únicamente login y GET con cuentas existentes; no se ejecutaron solicitudes contra desarrollo en esta etapa.

### Crear y consultar un ticket

```http
POST /api/tickets
Content-Type: application/json

{
  "title": "El portátil no arranca",
  "description": "La pantalla permanece apagada al encender el equipo.",
  "priority": "High",
  "category": "Hardware"
}
```

Respuesta ilustrativa: `201 Created`, con `Location: /api/tickets/1`.

```json
{
  "id": 1,
  "createdByUserId": null,
  "assignedToUserId": null,
  "title": "El portátil no arranca",
  "description": "La pantalla permanece apagada al encender el equipo.",
  "createdAt": "2026-10-20T10:00:00Z",
  "status": "Open",
  "priority": "High",
  "category": "Hardware"
}
```

El ID y la fecha los genera el servidor. `GET /api/tickets/1` devuelve el mismo formato; el listado devuelve estos objetos dentro de `items`, junto con los metadatos de paginación.

`priority` y `category` son opcionales en POST: si se omiten, se asignan `Medium` y `Other`. Enviar `null` explícitamente no es válido. Los nombres de estados, prioridades y categorías se aceptan con la capitalización mostrada; los números y nombres desconocidos se rechazan.

### Creador y asignación — etapa 8.3

POST /api/tickets conserva `createdByUserId` en el DTO por compatibilidad, pero ignora su valor. Siempre registra como creador al usuario autenticado y deja AssignedToUserId en NULL. No se permite atribuir tickets a otro usuario, tampoco a Admin. Un valor de tipo JSON incompatible sigue produciendo 400 durante la deserialización.

```json
{
  "title": "Pantalla sin señal",
  "description": "Revisar el equipo.",
  "createdByUserId": 3
}
```

Todas las respuestas que contienen un ticket añaden `createdByUserId` y `assignedToUserId` (número o null). Las navegaciones de EF no se serializan. El listado conserva `{items,totalCount,page,pageSize,totalPages}`. PUT de tickets sigue editando solo Title/Description; ni este ni los PATCH de clasificación cambian el creador o la asignación.

Asignar o reasignar requiere una petición de Admin; el usuario destino debe ser Technician o Admin:

```http
PATCH /api/tickets/123/assignment
Content-Type: application/json

{"assignedToUserId": 5}
```

Desasignar requiere null explícito:

```json
{"assignedToUserId": null}
```

Campo omitido, tipo incorrecto, usuario inexistente o rol User: 400. Ticket inexistente con cuerpo válido: 404. Éxito: 200 con ticket completo; repetir la asignación válida o la desasignación no guarda cambios. Si un usuario desaparece entre la validación y el guardado, una violación de las nuevas FK devuelve 409. No cambia el estado ni hay restricciones adicionales por estado.

PUT /api/users/{id} requiere Admin y rechaza con 409 el cambio a User mientras existan tickets asignados. Las FK siguen siendo opcionales, independientes e indexadas, con ON DELETE RESTRICT y sin borrado en cascada.

La asignación mantiene la transacción ReadCommitted y FOR SHARE sobre el usuario destino hasta commit. Se permiten asignaciones a tickets distintos del mismo técnico. PUT utiliza FOR NO KEY UPDATE antes de comprobar asignaciones; ambos mantienen el bloqueo hasta finalizar la transacción. No hay garantía de rol frente a SQL externo que eluda la regla HTTP. Asignaciones simultáneas al mismo ticket siguen la última escritura.

### Revisar y aplicar AddTicketUserRelations manualmente

Migración `20261021120235_AddTicketUserRelations`: dos columnas integer NULL en Tickets, dos índices y dos FK a Users con RESTRICT. Los tickets anteriores conservan sus campos y reciben null en las nuevas relaciones; no hay backfill, usuarios ficticios ni reconstrucción de tabla. Down elimina las dos relaciones/columnas y pierde sus valores, conservando los demás datos de tickets.

Las consultas y relaciones de tickets necesitan ese esquema. La etapa 9.1 requiere además AddUserPasswordHash, cuya revisión previa está en [AUTHENTICATION.md](docs/AUTHENTICATION.md). Revisar el SQL y las migraciones pendientes; el DDL requiere bloqueos sobre Tickets durante su aplicación. Estos comandos son para revisión y ejecución manual del usuario; no se han aplicado migraciones en esta sesión:

```bash
dotnet ef migrations script AddUsers AddTicketUserRelations --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext --output /tmp/helpdesk-ticket-user-relations-review.sql
```

Ejecutar primero la suite PostgreSQL con la conexión segura de pruebas descrita en la guía. Tras revisar el resultado, respaldar la base de desarrollo y aplicar manualmente la migración durante una parada coordinada de la API:

```bash
dotnet ef database update AddTicketUserRelations --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext -- --environment Development
```

Comparar el número de tickets antes/después, comprobar los nuevos campos null en los tickets previos y verificar GET, creación, asignación, desasignación y rechazo de roles/IDs inválidos con datos de prueba propios. Los clientes con esquemas JSON estrictos deberán admitir los dos campos nuevos.

### Filtrar y paginar tickets

```http
GET /api/tickets?status=Open&priority=High&category=Hardware&search=portátil&page=1&pageSize=10
```

Todos los parámetros son opcionales y combinables. `status`, `priority` y `category` aceptan los nombres exactos documentados. `search` busca una subcadena literal en Title o Description sin distinguir mayúsculas/minúsculas; `%`, `_` y `\` se tratan como texto. Una cadena vacía no añade filtro; los espacios se conservan.

`page` es un entero positivo (por defecto 1); `pageSize` está entre 1 y 100 (por defecto 10). Valores vacíos, incorrectos o parámetros conocidos repetidos devuelven `400` con `{"error":"mensaje explicativo"}`. También se rechaza una combinación cuyo desplazamiento `(page - 1) * pageSize` supere `2147483647`.

**Cambio de contrato:** GET /api/tickets ya no devuelve un array; los clientes deben leer `items`:

```json
{
  "items": [],
  "totalCount": 0,
  "page": 1,
  "pageSize": 10,
  "totalPages": 0
}
```

Cada elemento conserva todas las propiedades del ticket. `totalCount` cuenta los resultados filtrados antes de paginar; `totalPages` es su división entre `pageSize` redondeada hacia arriba, o 0 sin resultados. Una página posterior a la última devuelve `items: []` conservando los totales. El orden es CreatedAt descendente y después Id descendente. El orden resuelve empates; las escrituras concurrentes pueden desplazar resultados entre peticiones de páginas.

EF Core ejecuta los filtros, la búsqueda PostgreSQL `ILIKE`, el conteo y la paginación en el servidor mediante consultas parametrizadas y `AsNoTracking`. No se cargan todos los tickets en memoria. Conteo y lectura son dos consultas: pueden observar cambios concurrentes entre ambas.

### Actualizar información y clasificación

PUT recibe únicamente los textos editables:

```http
PUT /api/tickets/1
Content-Type: application/json

{"title":"Portátil pendiente de revisión","description":"Se ha comprobado la alimentación del equipo."}
```

Los cambios de clasificación se realizan por separado:

```http
PATCH /api/tickets/1/status
Content-Type: application/json

{"status":"InProgress"}
```

```http
PATCH /api/tickets/1/priority
Content-Type: application/json

{"priority":"Critical"}
```

```http
PATCH /api/tickets/1/category
Content-Type: application/json

{"category":"Other"}
```

Cada actualización válida devuelve `200` con el ticket completo y los demás campos conservados. Por ejemplo, solicitar `Closed` para un ticket que sigue en `Open` devuelve `409`:

```json
{
  "error": "No se permite cambiar el estado de Open a Closed.",
  "currentStatus": "Open",
  "requestedStatus": "Closed"
}
```

Un cuerpo JSON incorrecto o incompatible con el DTO devuelve `400` con una respuesta breve:

```json
{"error":"El cuerpo JSON de la petición no es válido."}
```

## Ejecución local

### Requisitos

- SDK de .NET 10.
- PostgreSQL local; la configuración documentada utiliza PostgreSQL 16.
- Git y acceso a NuGet para restaurar dependencias.
- `psql` para preparar la base; Python 3 para el ejemplo de configuración segura siguiente.

Los comandos se ejecutan desde la raíz del repositorio. Los ejemplos de terminal utilizan Bash.

```bash
git clone https://github.com/Ancodi1/HelpDeskt.NET.git HelpDesk.NET
cd HelpDesk.NET
dotnet restore HelpDesk.NET.slnx
dotnet tool restore --tool-manifest dotnet-tools.json
```

### PostgreSQL y User Secrets

Preparar manualmente una base de desarrollo `helpdesk_db` y un usuario dedicado `helpdesk_user`, con permisos para aplicar sus migraciones. La [guía de PostgreSQL](docs/POSTGRESQL.md) incluye los comandos de creación y comprobación de conexión con `psql`.

La API utiliza `ConnectionStrings:DefaultConnection`. El proyecto ya tiene `UserSecretsId`: no es necesario inicializarlo de nuevo. Guardar la cadena fuera de Git mediante User Secrets; este mecanismo es para desarrollo y no cifra los datos.

Formato de referencia, con un marcador que debe sustituirse localmente:

```text
Host=localhost;Port=5432;Database=helpdesk_db;Username=helpdesk_user;Password=<CONTRASEÑA_LOCAL>
```

Para guardarla sin mostrarla ni escribir su valor en el historial, usar entrada oculta y enviar JSON por stdin a Secret Manager:

```bash
python3 - <<'PY'
import getpass
import json
import subprocess

connection = getpass.getpass('Cadena de conexión local completa: ')
if not connection.strip():
    raise SystemExit('La cadena no puede estar vacía.')
result = subprocess.run(
    ['dotnet', 'user-secrets', 'set', '--project', 'HelpDesk.Api/HelpDesk.Api.csproj'],
    input=json.dumps({'ConnectionStrings:DefaultConnection': connection}),
    text=True, capture_output=True,
)
if result.returncode:
    raise SystemExit('No se pudo guardar la configuración local.')
print('Configuración local guardada.')
PY
```

La cadena debe respetar la sintaxis Npgsql, especialmente si la contraseña contiene punto y coma o comillas. No guardar credenciales en `appsettings*.json` ni compartir la salida de `dotnet user-secrets list`.

### Configuración JWT obligatoria

Antes de arrancar, configurar `Jwt:Issuer`, `Jwt:Audience` y `Jwt:SigningKeyBase64` fuera del repositorio. La [guía JWT para Linux Mint](docs/AUTHENTICATION.md#configuración-en-linux-mint) genera la clave criptográfica sin mostrarla y explica cómo probar el login con una cuenta existente sin escribir datos. La API falla al arrancar si falta configuración; no hay clave predeterminada. Las políticas protegen tickets y gestión de usuarios según la matriz anterior; el rol se contrasta en PostgreSQL en cada petición protegida.

### Migraciones y arranque

Con la base creada y la conexión configurada, revisar las migraciones antes de aplicarlas a una base con datos. Status, Priority y Category se añaden con valores predeterminados para los tickets anteriores: `Open`, `Medium` y `Other`.

```bash
dotnet build HelpDesk.NET.slnx
dotnet ef database update --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext -- --environment Development
dotnet run --project HelpDesk.Api --launch-profile http
```

El perfil `http` activa `Development`, carga User Secrets y escucha en `http://localhost:5085`. La API no aplica migraciones automáticamente al arrancar.

```bash
curl http://localhost:5085/api/health
```

Respuesta esperada: `{"status":"ok"}`. Este endpoint comprueba que la API responde; no verifica la conexión con PostgreSQL.

## Pruebas automatizadas

La suite actual contiene **489 casos**: **216 sin PostgreSQL superados** y **273 PostgreSQL descubiertos sin ejecutar** por falta de conexión segura en esta sesión. Incluye JWT, protección HTTP, permisos por rol/recurso, administración y concurrencia; el comportamiento PostgreSQL nuevo queda pendiente de ejecución.

Las pruebas unitarias verifican transiciones, validaciones y mapeos. Una prueba HTTP de salud utiliza WebApplicationFactory y TestServer en memoria, sin arrancar Kestrel manualmente. Para ejecutar todos los casos que no necesitan base de datos:

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL'
```

### Integración con PostgreSQL aislado

Las pruebas del CRUD utilizan exclusivamente `helpdesk_test_db`, un usuario dedicado `helpdesk_test_user` y un servidor local. Requieren una cadena explícita en `HELPDESK_TEST_CONNECTION_STRING`, almacenada fuera del repositorio. No utilizan la conexión de desarrollo como alternativa.

Seguir primero la [guía de pruebas PostgreSQL](docs/POSTGRESQL_TESTS.md) para crear manualmente la base y el rol, configurar la variable y revisar sus permisos. La fixture valida el destino antes de conectar, comprueba su identidad y privilegios y aplica las migraciones existentes solo a la base de pruebas. Los tests no crean ni eliminan bases; preparan tickets con GUID y limpian únicamente sus propios registros.

Con esa configuración preparada:

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category=PostgreSQL'
```

Para ejecutar toda la suite con el mismo entorno seguro:

```bash
dotnet test HelpDesk.NET.slnx
```

Si falta la cadena o no supera la validación del destino, los casos PostgreSQL fallan antes de conectar. Para trabajar sin ella, utilizar el filtro de exclusión anterior.

## Próximos pasos

Previsión de evolución, pendiente de implementación:

- Comentarios, historial y evolución de la gestión de usuarios.
- Activación segura de cuentas privilegiadas y evolución de la revocación de JWT, pendientes de solicitud.
- Interfaz web para gestionar los tickets.

## Documentación

- [Contraseñas, registro y revisión de la migración](docs/AUTHENTICATION.md).
- [Progreso y verificaciones](docs/PROGRESS.md).
- [Arquitectura y decisiones](docs/ARCHITECTURE.md).
- [Preparación local de PostgreSQL](docs/POSTGRESQL.md).
- [Pruebas con PostgreSQL aislado](docs/POSTGRESQL_TESTS.md).

Los registros fechados describen etapas anteriores. En particular, las referencias de la guía inicial a escrituras en memoria son históricas: el CRUD actual utiliza EF Core y PostgreSQL.
