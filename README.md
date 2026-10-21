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
- Consulta, creación y actualización REST de usuarios con roles `User`, `Technician` y `Admin`.
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

`User` tiene Id identity, Name obligatorio (máximo 100), Email obligatorio (máximo 254), Role con valor inicial `User` y CreatedAt en UTC. EF guarda Role como texto y CreatedAt como `timestamp with time zone`. Los endpoints utilizan DTOs independientes de la entidad. Los tickets pueden referenciar al usuario como creador o asignado. No hay DELETE de usuarios, autenticación ni contraseñas.

Email se normaliza mediante `Trim().ToLowerInvariant()`: se pierde su capitalización original. El índice único y el CHECK de PostgreSQL conservan la política canónica de la etapa 8.1. POST/PUT rechazan campos ausentes, null, vacíos, espacios y longitudes excesivas. Name conserva el texto recibido y se mide antes de recortarlo; Email se mide después de normalizarlo.

El formato de correo se comprueba con `MailAddress.TryCreate`, sin nombres de presentación ni espacios internos. Se requiere dominio con al menos dos etiquetas ASCII de 1–63 caracteres, letras/dígitos/guiones y sin guiones en los extremos. Dominios internacionalizados deben llegar en punycode. Es una comprobación de sintaxis razonable, no de existencia ni entrega; no se normalizan alias, puntos de proveedor ni equivalencias Unicode.

Role admite los nombres exactos `User`, `Technician` y `Admin`; números, null y nombres desconocidos devuelven 400. POST usa User al omitir Role. PUT requiere los tres campos editables y conserva Id/CreatedAt; los campos JSON adicionales se ignoran. Para un ID inexistente, PUT devuelve 404 antes de validar los campos del DTO, siguiendo la convención de tickets.

Los GET usan AsNoTracking y proyectan UserResponseDto (`id`, `name`, `email`, `role`, `createdAt`); el listado es un array ordenado por Id ascendente, sin paginación en esta etapa. POST devuelve 201 y Location; PUT devuelve 200. Duplicados devuelven 409 con `{"error":"Ya existe un usuario con ese correo electrónico."}`. Una consulta previa excluye al propio usuario en PUT y la captura de SQLSTATE 23505 del índice IX_Users_Email cubre carreras concurrentes; otros errores PostgreSQL no se convierten en conflictos.

El desarrollador confirma las migraciones hasta AddUsers aplicadas a desarrollo y las 318 pruebas anteriores superadas. La nueva AddTicketUserRelations está generada y revisada, sin aplicar en esta sesión.

## API

URL del perfil HTTP local: `http://localhost:5085`.

| Método | Ruta | Comportamiento |
| --- | --- | --- |
| GET | `/api/users` | `200` con array de DTOs ordenado por Id, incluido `[]` |
| GET | `/api/users/{id}` | `200` con DTO o `404` |
| POST | `/api/users` | `201` con DTO y Location, `400` o `409` |
| PUT | `/api/users/{id}` | `200` con DTO, `400`, `404` o `409` |
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

### Comprobación manual de usuarios

Con la API arrancada y sobre una base elegida para pruebas manuales, estos comandos crean y actualizan un usuario. No se han ejecutado contra desarrollo en esta etapa. Sustituir `123` por el ID devuelto por POST y utilizar un correo de ejemplo distinto en cada ejecución:

```bash
curl -i http://localhost:5085/api/users
curl -i -X POST http://localhost:5085/api/users -H 'Content-Type: application/json' -d '{"name":"Usuario de ejemplo","email":"manual-82@example.invalid"}'
curl -i http://localhost:5085/api/users/123
curl -i -X PUT http://localhost:5085/api/users/123 -H 'Content-Type: application/json' -d '{"name":"Usuario actualizado","email":"manual-82@example.invalid","role":"Technician"}'
```

Repetir POST con el mismo correo en mayúsculas permite comprobar 409. Enviar Name vacío, Email sin formato o Role desconocido devuelve 400 con JSON `error`. GET/PUT sobre un ID inexistente devuelve 404. No hay DELETE de usuarios para retirar este registro desde la API.

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

POST /api/tickets admite `createdByUserId` opcional. Omitirlo o enviar null conserva las peticiones anteriores. Cualquier rol puede crear tickets; un ID inexistente devuelve 400 con JSON `error`. No se infiere ningún usuario autenticado. La creación deja el ticket sin asignar, aunque lleguen campos adicionales.

```json
{
  "title": "Pantalla sin señal",
  "description": "Revisar el equipo.",
  "createdByUserId": 3
}
```

Todas las respuestas que contienen un ticket añaden `createdByUserId` y `assignedToUserId` (número o null). Las navegaciones de EF no se serializan. El listado conserva `{items,totalCount,page,pageSize,totalPages}`. PUT de tickets sigue editando solo Title/Description; ni este ni los PATCH de clasificación cambian el creador o la asignación.

Asignar o reasignar requiere Technician o Admin:

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

PUT /api/users/{id} devuelve 409 si se solicita Role=User mientras tiene tickets asignados, sin guardar tampoco los demás campos. Primero hay que reasignarlos o desasignarlos. Ser solo creador no limita cambios de rol. Las FK son opcionales, independientes e indexadas; PostgreSQL impide borrar usuarios referenciados mediante ON DELETE RESTRICT, sin borrar tickets en cascada. No se añade DELETE de usuarios.

**Límite de consistencia:** las comprobaciones de rol y asignaciones son consultas previas sin bloqueo común. Un cambio de rol concurrente puede superar ambas comprobaciones y dejar un usuario User con tickets asignados; una escritura SQL directa también puede hacerlo. Las FK garantizan existencia, no rol. Un test controlado documenta esta carrera; no se promete consistencia transaccional para ella. Asignaciones simultáneas al mismo ticket siguen la última escritura que se guarde.

### Revisar y aplicar AddTicketUserRelations manualmente

Migración `20261021120235_AddTicketUserRelations`: dos columnas integer NULL en Tickets, dos índices y dos FK a Users con RESTRICT. Los tickets anteriores conservan sus campos y reciben null en las nuevas relaciones; no hay backfill, usuarios ficticios ni reconstrucción de tabla. Down elimina las dos relaciones/columnas y pierde sus valores, conservando los demás datos de tickets.

La API actualizada necesita el esquema nuevo antes de utilizarse, incluido PUT de usuarios. Revisar el SQL y las migraciones pendientes; el DDL requiere bloqueos sobre Tickets durante su aplicación. Estos comandos son para revisión y ejecución manual del usuario; no se han aplicado migraciones en esta sesión:

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

La suite contiene **365 casos**: 120 sin PostgreSQL y 245 de integración PostgreSQL. La última verificación de esta etapa registra **120 superados y 245 descubiertos sin ejecutar** por falta de configuración de pruebas; no acredita una ejecución completa de los 365.

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
- Autenticación y autorización.
- Interfaz web para gestionar los tickets.

## Documentación

- [Progreso y verificaciones](docs/PROGRESS.md).
- [Arquitectura y decisiones](docs/ARCHITECTURE.md).
- [Preparación local de PostgreSQL](docs/POSTGRESQL.md).
- [Pruebas con PostgreSQL aislado](docs/POSTGRESQL_TESTS.md).

Los registros fechados describen etapas anteriores. En particular, las referencias de la guía inicial a escrituras en memoria son históricas: el CRUD actual utiliza EF Core y PostgreSQL.
