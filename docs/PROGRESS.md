# Progreso — HelpDesk.NET

Última actualización: 2026-10-27.

## Estado vigente — fase 10.3.2 implementada

Frontend con listado, creación y detalle de tickets en `/tickets`, `/tickets/new` y `/tickets/:id`. Se reutilizan el servicio, los contratos y los colores de estado/prioridad. El formulario usa los cuatro campos efectivos del POST (title, description, priority, category), bloquea envíos duplicados y redirige al ID validado de la respuesta 201 con confirmación. El detalle muestra exclusivamente los datos autorizados por la API y conserva los filtros al volver al listado.

Los tres roles autenticados pueden crear. La API fija creador, estado inicial y fecha, y limita el detalle como el listado. Un 404 se trata igual para inexistente y no visible. Sin cambios de autenticación, backend, base de datos, dependencias o pruebas anteriores; sin edición, eliminación o asignación.

Se mantienen login, registro y sesión solo en memoria y la página de inicio/health. Las pruebas de frontend usan HTTP simulado, sin crear tickets reales. No se ejecutan migraciones ni pruebas PostgreSQL. El límite de idempotencia del POST y los pasos manuales para un destino aislado/autorizado se documentan en README. Los resultados históricos de 9.4 (538 pruebas) siguen como antecedente.

## Etapa 9.4 — completada (base del backend)

Cierre funcional confirmado por el usuario el 27 de octubre de 2026: primer administrador creado con `HelpDesk.AdminBootstrap`, ID 3 y rol Admin. Acceso recuperado mediante una herramienta temporal externa al repositorio; esa intervención no incorpora una funcionalidad permanente de recuperación. Se conservan tres usuarios y seis tickets. API arrancada en Development, login HTTP 200, directorio administrativo sin token HTTP 401 y con JWT del administrador HTTP 200; cuenta ID 3 identificada correctamente en la respuesta.

La consola y la API comparten `PasswordHasher<User>` V3 y 210.000 iteraciones. La contraseña debe tener entre 15 y 128 unidades UTF-16; se introduce dos veces sin eco. Procedimiento operativo en [AUTHENTICATION.md](AUTHENTICATION.md#etapa-94-inicialización-del-primer-admin-mediante-cli).

El usuario confirma la suite completa superada: **257 pruebas sin PostgreSQL y 281 con PostgreSQL; total 538 superadas y 0 fallidas**. Build anterior: 0 errores y 0 advertencias. Auditoría de aislamiento PostgreSQL superada. Las referencias a pruebas pendientes en los apartados históricos siguientes describen el estado de aquellas sesiones y quedan resueltas por esta confirmación final.

## Funcionalidades completadas

- Contexto educativo, reglas de trabajo y decisiones iniciales documentados.
- Solución `HelpDesk.NET.slnx` creada.
- Proyecto ASP.NET Core Web API `HelpDesk.Api` creado para `net10.0` y añadido a la solución.
- Arranque mínimo en `Program.cs`, sin el ejemplo WeatherForecast.
- Primer endpoint Minimal API: `GET /api/health`, que devuelve HTTP 200 y JSON `{"status":"ok"}` mediante `app.MapGet()`.
- Configuración de aplicación y perfil HTTP local conservados. Dependencias de EF Core y PostgreSQL añadidas en la etapa de preparación.

- Primer modelo de dominio `Ticket` creado en `HelpDesk.Api/Models/Ticket.cs`, con únicamente `Id` (`int`), `Title` (`string`), `Description` (`string`) y `CreatedAt` (`DateTime`).

- Endpoint `GET /api/tickets`: listado paginado con filtros combinables y búsqueda literal sin distinguir mayúsculas; devuelve `items`, `totalCount`, `page`, `pageSize` y `totalPages` (etapa del 21 de octubre).

- Endpoint `GET /api/tickets/{id}`: recibe `int id` y `HelpDeskDbContext`, y consulta PostgreSQL con `await dbContext.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == id)`. Devuelve HTTP 200 con el ticket o HTTP 404 sin cuerpo si no existe.

- Endpoint `POST /api/tickets`: recibe `CreateTicketDto` y `HelpDeskDbContext` mediante DI, conserva `ValidateTicket` y construye un `Ticket` con los textos y `CreatedAt = DateTime.UtcNow`, sin asignar ID. Usa `dbContext.Tickets.Add(ticket)` y `await dbContext.SaveChangesAsync()`; PostgreSQL genera el ID y EF Core lo incorpora al objeto antes de devolver HTTP 201 con JSON y cabecera `Location` mediante `Results.Created`.

- Validación compartida de POST y PUT mediante la función local `ValidateTicket` en `Program.cs`: título obligatorio con máximo de 100 caracteres y descripción obligatoria. Se rechazan campos ausentes, `null`, vacíos o compuestos solamente por espacios con HTTP 400 y JSON `{"error":"mensaje explicativo"}`. Se devuelve el primer error sin añadir ni modificar tickets.

- Primer DTO `CreateTicketDto` creado en `HelpDesk.Api/DTOs/CreateTicketDto.cs`, con únicamente `Title` y `Description`. Separa la entrada del POST del modelo de dominio.

- Endpoint `PUT /api/tickets/{id}` con `UpdateTicketDto` (`Title` y `Description`): inyecta `HelpDeskDbContext`, busca con `FirstOrDefaultAsync`, devuelve 404 si no existe y conserva `ValidateTicket` con respuesta 400. Actualiza únicamente los textos de la entidad seguida por EF Core y guarda con `SaveChangesAsync`. Conserva ID y fecha de creación y devuelve HTTP 200 con el ticket actualizado.

- Endpoint `DELETE /api/tickets/{id}`: consulta PostgreSQL con `FirstOrDefaultAsync`, devuelve 404 si no existe o elimina mediante `Remove` y `SaveChangesAsync`, devolviendo 204 sin cuerpo.

- Preparación de PostgreSQL y EF Core: paquetes EF Core/Design `10.0.12` y Npgsql EF Core `10.0.3`; `Data/HelpDeskDbContext.cs`, `DbSet<Ticket>` y mapeo del modelo; contexto registrado mediante DI y cadena de conexión externa `ConnectionStrings:DefaultConnection` con User Secrets o variable de entorno. Guía Linux Mint 22.3 en `docs/POSTGRESQL.md`. Migración `InitialCreate` aplicada por el desarrollador; los dos GET ya consultan PostgreSQL.

`Ticket` se utiliza para la colección y las respuestas; POST y PUT reciben sus respectivos DTOs. Todo el CRUD utiliza PostgreSQL; la lista temporal ya no existe.

## Cierre de sesión — 8 de octubre de 2026

Integración local de EF Core con PostgreSQL confirmada. El proyecto sigue en `net10.0`, con EF Core/Design 10.0.12, Npgsql EF Core 10.0.3 y herramienta local `dotnet-ef` 10.0.12. `HelpDeskDbContext` se registra con `AddDbContext`/`UseNpgsql`, recibe opciones por constructor y expone `DbSet<Ticket>`. Configura clave identity, título obligatorio/máximo 100, descripción obligatoria y fecha UTC (`timestamp with time zone`). La conexión se obtiene de `ConnectionStrings:DefaultConnection`, sin credenciales en archivos del repositorio.

PostgreSQL 16.15 y la base `helpdesk_db` se verificaron mediante SELECT. El historial `__EFMigrationsHistory` contiene `20261008093411_InitialCreate`: la migración está aplicada. Los archivos de migración y snapshot están presentes localmente. Esta revisión no aplicó migraciones ni escribió datos.

Los dos GET están implementados con EF Core: contexto inyectado, `ToListAsync` para el listado y `FirstOrDefaultAsync` por ID. POST, PUT y DELETE siguen utilizando `List<Ticket>` con los tres ejemplos iniciales, DTOs y validación manual compartida. No se utiliza `SaveChanges` todavía. Los cambios de esos endpoints no se reflejan en GET; la persistencia de escrituras no está implementada.

Comprobaciones de cierre:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet ef migrations list --no-build --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext -- --environment Development`: consulta del historial correcta; `InitialCreate` aparece aplicada, sin marca Pending.
- SELECT de solo lectura confirmó `helpdesk_db` y PostgreSQL `16.15`.
- Arranque en Development con User Secrets. GET `/api/tickets`: HTTP 200, `application/json` y `[]`. GET `/api/tickets/2147483647`: HTTP 404 sin cuerpo. Health: HTTP 200 y JSON esperado. Verificados mediante aserciones y consultas SQL registradas por EF Core.
- Tabla vacía: no se comprobó la rama 200 del GET por ID existente. No se añadieron datos de prueba.
- Se revisaron los 20 archivos versionados o no ignorados, configuración y migraciones buscando contraseñas, cadenas con credenciales, claves privadas y nombres de archivos sensibles. No se detectaron secretos; el único ejemplo de contraseña usa un marcador. No se mostró ni modificó User Secrets.
- Staging vacío: ningún archivo preparado para commit. Hay cambios locales y archivos nuevos de la integración pendientes de versionar; no se hizo commit ni push.
- No se ejecutó `dotnet test`: no hay proyectos de tests automatizados. Las comprobaciones HTTP/SQL se ejecutaron con autorización fuera del sandbox por el bloqueo de sockets. Servidor detenido.
- Solo se actualizó documentación en este cierre; código, arquitectura y funcionalidades no se modificaron.

## Avance — 13 de octubre de 2026

POST migrado a EF Core, conservando DTO, validación y contrato HTTP. Solo cambió este endpoint en `Program.cs`; PUT, DELETE y su lista temporal permanecen intactos. No se modificó el modelo ni se generaron o aplicaron migraciones. El cierre del 8 de octubre anterior es un registro histórico.

Comprobaciones realizadas:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx --no-restore`: terminó con código 0; no hay proyectos de tests ni casos automatizados ejecutados.
- Nueve POST inválidos (campos ausentes, null, vacíos, espacios y título de 101 caracteres): HTTP 400 con mensaje `error`; listado idéntico antes y después.
- Un POST válido: HTTP 201, textos esperados, fecha UTC, ID 1 generado y `Location: /api/tickets/1`.
- Ambos GET: HTTP 200 y ticket creado recuperado. Tras detener y arrancar nuevamente la API, ambos GET conservan el ticket.
- La comparación de fechas tiene en cuenta que PostgreSQL conserva microsegundos (seis decimales), mientras el objeto recién creado puede devolver siete decimales de .NET.
- La tabla estaba vacía antes de las pruebas. Se dejó un único ticket de prueba persistido; no se eliminaron datos ni se llamó a PUT o DELETE.
- El sandbox bloqueó sockets al arrancar la API y realizar peticiones. Las comprobaciones HTTP se completaron fuera del sandbox con autorización. API detenida al terminar.
- No se hizo commit ni push.

## Avance de PUT — 13 de octubre de 2026

PUT migrado a PostgreSQL con seguimiento de entidades de EF Core y guardado asíncrono. Conserva DTO, validación y respuestas 200/400/404; solo actualiza Title y Description. GET, POST, DELETE y la lista temporal no se modificaron en esta etapa. Sin cambios de modelo, migraciones ni capas adicionales.

Comprobaciones realizadas:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx --no-restore`: código 0; no existen proyectos de tests automatizados ni casos ejecutados.
- Ticket exclusivo de prueba creado mediante POST (ID 5). PUT válido: 200 con textos actualizados, conservando ID y CreatedAt incluso al enviar esos campos adicionales en JSON. Repetir PUT mantiene el mismo resultado.
- Nueve PUT inválidos (campos ausentes, null, vacíos, espacios y título de 101 caracteres): 400 con mensaje de error. Un GET después de cada petición confirmó que el ticket no cambió.
- PUT por ID comprobado como inexistente: 404 sin cuerpo, tanto con textos válidos como con entrada inválida; se conserva la búsqueda antes de validar.
- GET por ID y listado confirmaron los cambios persistidos mediante nuevas peticiones. Los cuatro tickets previos permanecieron intactos; se dejó el ticket de prueba actualizado, sin borrar datos.
- El sandbox bloqueó el arranque y las peticiones por restricciones de sockets; las pruebas HTTP se completaron con autorización fuera del sandbox. API detenida al terminar.
- No se hizo commit ni push.

## Problemas conocidos

- `AddTicketStatus` aplicada por el desarrollador; seis tickets existentes verificados con `Open` antes de esta etapa. PATCH validado contra PostgreSQL en esta tarea.
- POST ya no depende de `Max + 1`: los anteriores problemas con lista vacía, reutilización del máximo eliminado y cálculo concurrente de IDs quedan resueltos al delegar la generación en PostgreSQL.
- SDK comprobado: .NET 10.0.112; runtime ASP.NET Core 10.0.12.
- `HelpDesk.Tests`: 16 pruebas unitarias de transiciones y una prueba de integración HTTP de health con WebApplicationFactory. Registro histórico de etapa 8.3: 120 casos sin PostgreSQL superados y 245 casos PostgreSQL descubiertos; la suite actual figura al final en etapa 9.1. Los de base no se ejecutaron en la etapa 8.3 por configuración ausente; guía en POSTGRESQL_TESTS.md.
- Conflicto MSB3277 resuelto: referencia explícita a Microsoft.EntityFrameworkCore.Relational 10.0.12 en la API, propagada a Tests. Restore, build y 16 tests sin advertencias.
- Las rutas sin un endpoint registrado devolverán 404.

## Próximos pasos

Estos pasos son orientativos y requieren una tarea solicitada:

1. Ampliar las funcionalidades del dominio únicamente mediante tareas solicitadas; las transiciones de estado ya están implementadas.
2. Incorporar comentarios e historial por tareas concretas. Relaciones y asignación de técnicos, endpoints GET/POST/PUT de usuarios, categorías, prioridades y transiciones ya están implementados.
3. Continuar el frontend Angular por fases solicitadas (la base 10.1 ya está implementada), ampliar las pruebas automatizadas e incorporar Docker cuando aporte valor. Autenticación, autorización y primer administrador ya están implementados.

Cada cambio importante debe actualizar este documento y, si afecta al diseño, `ARCHITECTURE.md`.

## Avance — 20 de octubre de 2026

La revisión local confirma CRUD completo con EF Core y PostgreSQL, incluido DELETE, sin lista temporal. Las cinco pruebas HTTP principales superadas son un resultado comunicado por el desarrollador; no se han repetido en esta tarea. Los apartados fechados anteriores conservan el estado histórico.

Añadidos `Models/TicketStatus.cs` (`Open`, `InProgress`, `Resolved`, `Closed`) y `Ticket.Status`, inicializado explícitamente en `Open`. El contexto convierte el enum a cadena y configura `Open` como valor predeterminado de la columna. Generada `20261020092721_AddTicketStatus`, con Designer y snapshot actualizados. No se han modificado endpoints, DTOs ni implementado reglas de transición.

Verificaciones:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx --no-restore`: código 0; no hay proyectos de tests ni casos automatizados ejecutados.
- Revisados Up, Down, Designer, snapshot y SQL generado. Up solo añade `Status` (`text NOT NULL DEFAULT 'Open'`): conserva filas y columnas existentes y asignará `Open` a los tickets previos al aplicarse. Down solo elimina esa columna y perdería sus valores de estado al revertir.
- La primera generación de SQL con `--no-build` no encontró la migración porque el ensamblado aún era el anterior; repetida correctamente tras el build.
- No se ha ejecutado `dotnet ef database update`, aplicado SQL ni comprobado el cambio sobre datos reales. No se ejecutaron pruebas HTTP contra el esquema anterior.
- La conversión a texto es de persistencia; JSON mantiene la serialización numérica predeterminada del enum. Las respuestas basadas en `Ticket` incorporarán `status` al utilizar el modelo actualizado.
- Sin commit ni push.

## Avance de PATCH de estado — 20 de octubre de 2026

El desarrollador confirmó `AddTicketStatus` aplicada y seis tickets existentes con `Open`. Añadidos `DTOs/UpdateTicketStatusDto.cs` con `string? Status` y `PATCH /api/tickets/{id}/status` en `Program.cs`. Consulta mediante `FirstOrDefaultAsync`, devuelve 404 si no existe y valida los nombres exactos con `Enum.IsDefined` antes de convertir con `Enum.Parse<TicketStatus>`. Solo asigna Status y guarda mediante `SaveChangesAsync`; responde 200 con Ticket. No hay reglas de transición.

Los valores desconocidos, cadenas numéricas, null, ausentes, vacíos, grafías distintas y combinaciones de nombres devuelven 400; un número JSON devuelve 400 durante la deserialización, antes del handler. La búsqueda precede a la validación de texto. Las respuestas mantienen el enum numérico en JSON. Los campos adicionales de entrada se ignoran y no modifican otras propiedades.

Verificaciones realizadas:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx --no-restore`: código 0, sin proyectos de tests ni casos automatizados ejecutados.
- PATCH con cada estado (`Open`, `InProgress`, `Resolved`, `Closed`): 200; GET independiente confirmó cada valor persistido.
- Ocho entradas inválidas: `Unknown`, número JSON 1, cadena `"1"`, null, campo ausente, vacío, `open` y `Open, Closed`: 400; GET después de cada petición confirmó que no había cambios.
- ID comprobado como inexistente mediante GET: PATCH con estado válido devolvió 404.
- Id, Title, Description y CreatedAt intactos en todas las respuestas y GET, incluso enviando otros valores para esas propiedades en PATCH.
- Estado original restaurado mediante PATCH; comparación final del listado completo confirmó los seis tickets idénticos al inicio, sin crear ni borrar registros.
- El sandbox bloqueó los sockets del servidor y del cliente; pruebas HTTP completadas fuera del sandbox con autorización. API de pruebas detenida.
- GET, POST, PUT y DELETE conservan sus handlers; se comprobó GET, sin repetir escrituras POST/PUT/DELETE en esta tarea. Sin nuevas migraciones, cambios de esquema, servicios ni repositorios. Sin commit ni push.

## Serialización JSON de enums — 20 de octubre de 2026

Configuración centralizada en `Program.cs` mediante `ConfigureHttpJsonOptions` y `JsonStringEnumConverter`. Las respuestas de las Minimal APIs representan TicketStatus con sus nombres, por ejemplo `"status":"Resolved"`. Las referencias anteriores a JSON numérico describen el comportamiento histórico. El enum C# y su conversión a texto en PostgreSQL permanecen intactos. El DTO de PATCH sigue siendo string nullable y conserva toda su validación explícita; no se modifican handlers, esquema, migraciones ni dependencias.

Verificación: `dotnet build HelpDesk.NET.slnx` correcto, 0 errores y 0 advertencias. `dotnet test HelpDesk.NET.slnx --no-restore` con código 0, sin proyectos de tests ni casos ejecutados. Pruebas HTTP: los cuatro estados devolvieron 200 con status como texto, incluido Resolved, y GET posterior confirmó cada valor y las demás propiedades intactas. Inventado, número JSON 1, cadena numérica, null, campo ausente, vacío, capitalización distinta y combinación de nombres devolvieron 400 sin cambios. ID inexistente: 404. Estado original restaurado y los seis tickets idénticos al inicio. No se repitieron POST, PUT ni DELETE; sus handlers permanecen intactos. Pruebas fuera del sandbox con autorización por la restricción de sockets ya comprobada; API detenida al terminar. Sin commit ni push.

## Errores del cuerpo JSON — 20 de octubre de 2026

Configuración centralizada en Program.cs: RouteHandlerOptions.ThrowOnBadRequest=false evita lanzar excepciones por fallos de enlace de las Minimal APIs; UseStatusCodePages completa únicamente respuestas 400 sin cuerpo con `{"error":"El cuerpo JSON de la petición no es válido."}`. Las respuestas de validación que ya contienen JSON permanecen intactas. No se capturan excepciones de negocio o PostgreSQL ni se convierten en 400. Development sigue activo. El mensaje genérico también cubre otros fallos de enlace que produzcan un 400 vacío; no cambia 404 ni 415.

Verificaciones:

- Build correcto: 0 errores, 0 advertencias. dotnet test con código 0, sin proyectos de tests ni casos ejecutados.
- En Development, PATCH con número JSON, JSON incompleto, array, null como cuerpo y cuerpo vacío: 400 con el JSON genérico exacto, sin trazas ni nombres de clases, incluso solicitando text/plain.
- POST y PUT con título numérico: mismo 400 genérico, sin escrituras.
- PATCH con Inventado, cadena numérica, estado null y campo ausente: 400 con el mensaje personalizado de estados existente.
- PATCH Resolved: 200; GET confirma Resolved como texto y otras propiedades intactas. Estado original restaurado; seis tickets idénticos al inicio, comparados por ID. La primera comparación falló por asumir orden del listado; corregida la prueba, sin modificar la consulta.
- GET inexistente: 404; health intacto. API detenida tras pruebas HTTP fuera del sandbox con autorización por restricción de sockets.
- Sin cambios de lógica de negocio, esquema, migraciones ni dependencias. Sin commit ni push.

## Reglas de transición de estados — 20 de octubre de 2026

Añadida `Models/TicketStatusTransitions.cs`, clase estática con una función pura IsAllowed que expresa las transiciones mediante switch. PATCH conserva DTO, consulta y validación; después de convertir el estado devuelve 200 inmediatamente si coincide con el actual, sin asignar ni llamar a SaveChangesAsync. Para cambios distintos consulta la regla y devuelve 409 con error, currentStatus y requestedStatus cuando no están permitidos. Solo los cambios permitidos asignan Status y guardan con EF Core.

Transiciones: Open → InProgress; InProgress → Resolved u Open; Resolved → Closed, InProgress u Open; Closed → Open. Cualquier otro cambio distinto se rechaza. Esto sustituye la libertad de transición descrita en etapas históricas anteriores.

Verificaciones:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx --no-restore`: código 0; sin proyectos de tests ni casos automatizados ejecutados.
- HTTP sobre PostgreSQL: ciclo Open → InProgress → Resolved → Closed → Open completo con 200 y GET independiente tras cada cambio.
- Comprobadas las 16 combinaciones: siete transiciones con 200, cinco prohibidas con 409 (incluida Open → Closed) y cuatro repeticiones con 200. Los 409 incluyen ambos estados y no modifican el ticket. La rama idempotente retorna antes del guardado, comprobado en el código.
- Estado Inventado, número JSON, cadena numérica, null, campo ausente y JSON incompleto: 400 con error, sin cambios. Ticket comprobado como inexistente: 404.
- Id, Title, Description y CreatedAt intactos; PATCH ignora valores adicionales para título e ID. Restaurado el estado original siguiendo transiciones permitidas. Comparación final por ID: los seis tickets idénticos al inicio, sin crear ni borrar datos.
- Pruebas HTTP fuera del sandbox con autorización por restricción de sockets. API de pruebas detenida. Sin repetir POST, PUT ni DELETE; sus handlers no se modifican.
- Sin cambios de esquema, migraciones, dependencias ni capas. Sin commit ni push.

## Primeras pruebas unitarias con xUnit — 20 de octubre de 2026

Creado `HelpDesk.Tests` para net10.0, añadido a `HelpDesk.NET.slnx` y referenciado a HelpDesk.Api mediante ProjectReference. Usa xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 y xunit.runner.visualstudio 3.1.4 de la plantilla local; retirados el test vacío y coverlet.collector por no necesitar cobertura en esta etapa. Sin mocking ni infraestructura adicional.

`TicketStatusTransitionsTests.cs` contiene tres Theory con InlineData: siete transiciones permitidas (Assert.True), cinco prohibidas (Assert.False) y cuatro estados iguales (Assert.False). Cubren las 16 combinaciones de los estados definidos. La igualdad no es una transición permitida para IsAllowed; el endpoint gestiona por separado la respuesta idempotente 200. No se modifica código de producción: enum, clase y método ya son públicos.

Verificaciones:

- `dotnet build HelpDesk.NET.slnx`: código 0, 0 errores, 1 advertencia MSB3277 por versiones transitivas de EF Core Relational 10.0.4 y 10.0.12. No se han cambiado dependencias de persistencia para corregirla fuera de este alcance.
- `dotnet test HelpDesk.NET.slnx`: código 0; 16 pruebas descubiertas y ejecutadas, 16 superadas, 0 fallidas y 0 omitidas. La misma advertencia aparece durante la compilación previa.
- SDK usado: 10.0.113; pruebas para net10.0 ejecutadas por VSTest 18.0.2.
- La plantilla requirió permiso para su caché de usuario. Build falló inicialmente en sandbox sin diagnóstico; test mostró bloqueo de sockets de los canales internos de MSBuild. Ambos comandos completados fuera del sandbox con autorización.
- No se arrancó la API, no se usó PostgreSQL ni se realizaron peticiones HTTP.
- `git diff --check`: correcto. Sin commit ni push.

## Alineación de EF Core Relational — 20 de octubre de 2026

Identificada la causa mediante `dotnet nuget why` y project.assets.json: Design 10.0.12 introducía Relational 10.0.12 en la API, pero PrivateAssets=all impedía propagar Design a Tests. Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 admite EF Core/Relational en el rango [10.0.4, 11.0.0), por lo que Tests resolvía Relational 10.0.4. La API compilada contra 10.0.12 entraba en conflicto con esa referencia durante la compilación de Tests.

Cambio mínimo: añadir PackageReference a Microsoft.EntityFrameworkCore.Relational 10.0.12 en HelpDesk.Api.csproj. Ahora ambos grafos resuelven 10.0.12 y la dependencia de ejecución ya no depende de Design para su alineación. Se mantienen net10.0, Npgsql 10.0.3, xUnit y demás versiones. No se suprime MSB3277.

Verificaciones completadas fuera del sandbox con autorización por las restricciones de MSBuild ya comprobadas:

- `dotnet restore HelpDesk.NET.slnx`: código 0, sin advertencias.
- `dotnet build HelpDesk.NET.slnx`: código 0, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx`: código 0, 16 superadas, 0 fallidas, 0 omitidas y sin advertencias.
- `dotnet nuget why HelpDesk.NET.slnx Microsoft.EntityFrameworkCore.Relational`: confirma 10.0.12 en API y Tests.
- `git diff --check`: correcto. Sin cambios en tests, endpoints, reglas o migraciones; sin API arrancada ni acceso a PostgreSQL. Sin commit ni push.

Las advertencias consignadas en la etapa anterior son resultados históricos, resueltos por este cambio.

## Primera prueba de integración HTTP — 20 de octubre de 2026

Añadido Microsoft.AspNetCore.Mvc.Testing 10.0.12 a HelpDesk.Tests. Program.cs expone `public partial class Program` como punto de entrada accesible para WebApplicationFactory; los handlers y su contrato no cambian.

`HealthEndpointTests.cs` incluye un Fact asíncrono que crea WebApplicationFactory<Program> en el entorno Testing y un HttpClient de TestServer. Solicita GET /api/health y comprueba HTTP 200, Content-Type application/json y un objeto JSON con una única propiedad status cuyo valor es ok. La comprobación estructural no depende de espacios ni formato del JSON. Client, respuesta, documento JSON y fábrica se liberan al terminar.

Health no solicita HelpDeskDbContext y su registro es diferido: no se ejecuta la configuración Npgsql ni se abre conexión. No ha sido necesario sustituir el contexto ni proporcionar credenciales. No se arranca Kestrel, no se abre un puerto HTTP ni se realizan operaciones contra PostgreSQL. Las 16 pruebas unitarias permanecen intactas. Sin pruebas CRUD, contenedores, cambios de migraciones o reglas de negocio.

Verificaciones realizadas fuera del sandbox con autorización por las restricciones de NuGet/MSBuild ya comprobadas:

- `dotnet restore HelpDesk.NET.slnx`: correcto, sin advertencias.
- `dotnet build HelpDesk.NET.slnx`: 0 errores, 0 advertencias.
- `dotnet test HelpDesk.NET.slnx`: 17 superadas (16 unitarias y una HTTP), 0 fallidas, 0 omitidas, sin advertencias.
- `git diff --check`: correcto. Sin commit ni push.

## Infraestructura PostgreSQL de pruebas — 20 de octubre de 2026

Implementada exclusivamente en HelpDesk.Tests/Integration: PostgreSqlTestSettings valida la conexión explícita de entorno sin fallback; TicketApiFactory reemplaza contexto, opciones y configurador EF original solo en Testing; PostgreSqlFixture verifica existencia, base/usuario efectivos y ausencia de privilegios elevados antes de aplicar las migraciones existentes. Destino admitido: PostgreSQL local, helpdesk_test_db, rol dedicado helpdesk_test_user con contraseña. Lista cerrada de claves; mensajes sin cadena completa ni contraseñas. El rol y la base deben prepararse manualmente. No se crean ni borran bases ni se usa la configuración de desarrollo.

Tres pruebas Category=PostgreSQL preparadas: POST 201 con verificación de persistencia y Location; GET 200 de un ticket insertado en la base; GET 404 de ID comprobado como ausente. Datos con título GUID por prueba y limpieza asíncrona limitada a ese título, con comprobación posterior. No se vacían tablas ni se alteran otros registros. La limpieza no puede ejecutarse si el proceso se interrumpe o la base deja de estar accesible; límites y preparación manual en POSTGRESQL_TESTS.md.

Verificaciones reales:

- HELPDESK_TEST_CONNECTION_STRING no configurada: no se abrió ninguna conexión PostgreSQL, no se verificó la existencia de la base ni se aplicaron migraciones. Las tres pruebas PostgreSQL NO se ejecutaron; no se declaran superadas.
- dotnet restore HelpDesk.NET.slnx y dotnet build HelpDesk.NET.slnx: correctos, 0 errores y 0 advertencias.
- dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL': 25 superadas, 0 fallidas y 0 omitidas. Incluye las 17 anteriores y ocho validaciones de configuración sin red. Las tres PostgreSQL se excluyen por filtro, no se omiten silenciosamente.
- Los tests PostgreSQL se solicitan con --filter 'Category=PostgreSQL'. Sin configuración válida, la fixture detiene esos tests con error antes de conectar. Ejecutar sin filtro también los incluye y exige configuración segura.
- git diff --check correcto. Comandos de restore/build/test fuera del sandbox con autorización por restricciones de MSBuild. Sin cambios de producción, paquetes, migraciones ni reglas; sin commit ni push.

## Depuración del validador PostgreSQL — 20 de octubre de 2026

Causa reproducida con prueba de regresión, inicialmente fallida en Validate línea 38: la fixture pasaba la salida normalizada a TicketApiFactory, que volvía a validarla. Validate añadía Include Error Detail=false, Persist Security Info=false y Enlist=false, pero su propia lista de claves rechazaba esas opciones. Corregido el validador para aceptar exclusivamente false en esas tres claves; la salida puede validarse repetidamente sin relajar base, usuario, host ni las opciones restantes. Fixture y fábrica revisadas, sin cambios.

Separados mensajes seguros para sintaxis, opción no permitida, valor inválido y configuración de seguridad incorrecta. Sin claves desconocidas, valores, contraseñas o inner exceptions en el diagnóstico. Añadidas pruebas de regresión, rechazo de las tres opciones con true, diagnósticos y conservación de contraseñas ficticias con punto y coma, comillas simples/dobles, signo igual y combinaciones usando NpgsqlConnectionStringBuilder. Guía actualizada para composición y quoting ADO.NET.

Resultados: restore y build correctos (0 errores, 0 advertencias); dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL': 39 superadas, 0 fallidas, 0 omitidas. La primera ejecución aislada de la regresión falló antes de corregir; después pasó con toda la suite sin base. Comandos fuera del sandbox con autorización. git diff --check correcto.

HELPDESK_TEST_CONNECTION_STRING no está disponible en este proceso: no se pudo comprobar la contraseña real ni atribuirle un error de escapado. No se conectó a PostgreSQL ni se ejecutaron los tres tests PostgreSQL. Pendiente ejecutar con la variable real tras validarla. Sin cambios en producción, migraciones o dependencias; sin commit ni push.

## Ampliación de integración PUT/PATCH/DELETE — 20 de octubre de 2026

TicketEndpointsTests añade 17 casos PostgreSQL a los tres existentes: PUT válido, título vacío, título de 101 caracteres y 404; PATCH Open → InProgress, InProgress → Resolved, Resolved → Closed, Closed → Open, Open → Closed con conflicto explicativo, repetición de cada uno de los cuatro estados, estado inválido y 404; DELETE válido con ticket de control y DELETE inexistente. Verifican respuestas, GET posterior y lectura AsNoTracking independiente. PUT conserva Id/CreatedAt/Status; PATCH conserva las otras propiedades; errores y repeticiones dejan el ticket completo intacto. DELETE comprueba ausencia real y conservación del ticket de control.

Reutilizados PostgreSqlFixture, TicketApiFactory y PostgreSqlTestSettings sin cambios. La limpieza registra títulos GUID antes de guardar y los IDs propios después, de modo que soporta cambios de título por PUT y fallos de aserciones. Solo borra por IDs propios o títulos exactos; no hay snapshots globales, borrados masivos ni dependencias del orden de otros tests. Los nuevos casos siguen en la clase existente, con instancias y scopes separados. La limpieza no está garantizada si se aborta el proceso o se pierde el acceso a la base.

Resultados reales:

- dotnet restore HelpDesk.NET.slnx: correcto.
- dotnet build HelpDesk.NET.slnx: 0 errores y 0 advertencias.
- dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL': 39 ejecutadas, 39 superadas, 0 fallidas y 0 omitidas.
- Descubrimiento mediante --list-tests --filter 'Category=PostgreSQL': 20 casos (tres anteriores y 17 nuevos), sin ejecutar fixture ni conexiones.
- HELPDESK_TEST_CONNECTION_STRING ausente: 0 pruebas PostgreSQL ejecutadas; no se declaran superadas. Pendientes de ejecución con configuración validada.
- git diff --check correcto. Verificaciones fuera del sandbox con autorización por restricciones de MSBuild. Sin cambios de producción, reglas, migraciones, paquetes ni seguridad; sin commit ni push.

## Prioridades de tickets — 20 de octubre de 2026

El desarrollador comunica las 59 pruebas anteriores superadas y cambios guardados en GitHub. La revisión local parte de un repositorio limpio; este registro distingue ese resultado de las comprobaciones ejecutadas en esta sesión.

Implementado TicketPriority (Low, Medium, High, Critical), Ticket.Priority inicializado en Medium y conversión EF a texto con default Medium. HasSentinel(Medium) hace explícito el valor que permite usar el default al insertar y evita tratar Low (cero en C#) como ausencia de valor. Nueva función pura TicketPriorityValidation.TryParse compartida por POST y PATCH, con nombres exactos y rechazo de null, números escritos como texto, vacíos y desconocidos. JSON sigue usando el conversor central de enums.

CreateTicketDto recibe Priority opcional como string nullable inicializado en Medium: omitirla conserva compatibilidad; null explícito es inválido. POST conserva validación de textos y añade validación de prioridad antes de registrar o guardar. Nuevo UpdateTicketPriorityDto y PATCH /api/tickets/{id}/priority: consulta y 404 si no existe, validación 400, retorno 200 sin SaveChangesAsync si coincide, o asignación exclusiva de Priority y guardado asíncrono. PUT sigue limitado a textos y las transiciones de Status no cambian.

Generada 20261020105220_AddTicketPriority, Designer y snapshot. Up añade exclusivamente Priority como text NOT NULL DEFAULT 'Medium': al aplicarse los tickets anteriores reciben Medium conservando sus otras columnas y filas. Down elimina solo Priority y perdería esos valores al revertir. Revisados código y SQL; no hay recreación de Tickets, borrado de registros ni aplicación de migraciones en esta sesión. Generación y script con configuración temporal de diseño Testing sin credenciales y sin conectar; no se usó helpdesk_db. Desarrollo necesita revisión y aplicación manual de la migración antes de usar el modelo actualizado.

Pruebas añadidas: 15 sin PostgreSQL (cuatro prioridades válidas, nueve entradas inválidas, defaults y metadatos EF/sentinel/conversión) y 29 HTTP PostgreSQL en TicketEndpointsTests.Priority.cs, parte de la misma clase, fixture y limpieza existentes. Cubren POST con las cuatro prioridades y errores; PATCH con las cuatro, repetición, prioridades inválidas/ausentes/null/numéricas, JSON incorrecto y 404, preservando otras propiedades. El POST existente comprueba Medium al omitir prioridad. Las comparaciones comunes incluyen Priority y PUT se prueba con prioridad High para detectar reinicializaciones.

Verificaciones reales:

- dotnet restore HelpDesk.NET.slnx: correcto.
- dotnet build HelpDesk.NET.slnx: 0 errores y 0 advertencias.
- dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL': 54 ejecutadas, 54 superadas, 0 fallidas y 0 omitidas.
- Listado con --list-tests --filter 'Category=PostgreSQL': 49 casos descubiertos, sin ejecución (20 anteriores y 29 nuevos). Total definido: 103 casos.
- HELPDESK_TEST_CONNECTION_STRING ausente en el proceso: no se ejecutaron las pruebas PostgreSQL ni se aplicó AddTicketPriority en ninguna base. Persistencia real pendiente en esta sesión. Al ejecutar los tests con configuración segura, la fixture aplicará la migración solo a helpdesk_test_db.
- SQL de AddTicketStatus a AddTicketPriority revisado: ALTER TABLE Tickets ADD Priority text NOT NULL DEFAULT Medium y registro de historial; no ejecutado. git diff --check correcto.
- Verificaciones de build/tests fuera del sandbox con autorización por restricciones de MSBuild. Sin cambios de dependencias o seguridad de pruebas; sin commit ni push.

## Categorías de tickets — 20 de octubre de 2026

Implementados TicketCategory (Hardware, Software, Network, Access, Other), Ticket.Category=Other y TicketCategoryValidation.TryParse para nombres exactos. EF convierte a texto y configura Other como default y sentinel, conservando Hardware (cero del enum) como valor explícito de inserción. El conversor JSON central sigue devolviendo enums como texto.

POST permite Category opcional mediante CreateTicketDto: omitida asigna Other; null explícito, números, vacíos y nombres desconocidos son inválidos. UpdateTicketCategoryDto y PATCH /api/tickets/{id}/category permiten cambiar solo Category: 404 si no existe, 400 si la entrada es inválida, 200 sin asignar ni guardar si coincide, o guardado exclusivo de Category para un cambio válido. No cambian PUT, las reglas de estados ni la lógica de prioridades.

Generada 20261020110201_AddTicketCategory con Designer y snapshot. Up contiene únicamente ADD Category text NOT NULL DEFAULT 'Other'. Al aplicarse, todos los tickets previos recibirán Other sin recrear Tickets ni borrar filas. Down solo elimina Category y perdería sus valores. Migraciones anteriores intactas. Generación y SQL de revisión con configuración temporal Testing sin credenciales ni conexiones. No se aplicó la migración a desarrollo ni a ninguna otra base en esta sesión. Antes de usar el modelo actualizado sobre el esquema anterior se debe revisar y aplicar manualmente la migración.

Pruebas: 16 casos sin PostgreSQL añadidos para cinco nombres válidos, nueve inválidos, defaults y mapeo/sentinel; 32 casos PostgreSQL nuevos para creación con cada categoría, errores, PATCH válido, repetición de cada categoría y 404. El POST previo verifica Other al omitir categoría. GET y consulta independiente comprueban persistencia; PATCH envía también propiedades ajenas y verifica que se ignoran. Comparaciones comunes incluyen Category, y PUT prepara una categoría Network para verificar su conservación. Todos los casos reutilizan la clase parcial, fixture, fábrica, validación y limpieza por GUID/ID existentes. Sin nuevas dependencias ni cambios de seguridad.

Resultados reales:

- dotnet restore HelpDesk.NET.slnx: correcto.
- dotnet build HelpDesk.NET.slnx: 0 errores y 0 advertencias.
- dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL': 70 ejecutadas y superadas, 0 fallidas, 0 omitidas.
- --list-tests --filter 'Category=PostgreSQL': 81 casos descubiertos, sin ejecución (49 anteriores y 32 nuevos). Total definido: 151.
- HELPDESK_TEST_CONNECTION_STRING no disponible en este proceso: no se abrieron conexiones ni se ejecutaron los tests PostgreSQL. No se declaran superados. La ejecución segura pendiente aplicará las migraciones solo a helpdesk_test_db mediante la fixture.
- SQL AddTicketPriority → AddTicketCategory revisado: ALTER TABLE Tickets ADD Category text NOT NULL DEFAULT Other y registro de historial, sin ejecutar. git diff --check correcto.
- Restore/build/test y listado fuera del sandbox con autorización por restricciones de MSBuild. Sin commit ni push.

## Filtros, búsqueda y paginación — 21 de octubre de 2026

GET /api/tickets admite status, priority, category, search, page y pageSize. Composición IQueryable con AsNoTracking, filtros combinables e ILIKE literal parametrizado sobre Title/Description. CountAsync se ejecuta antes de ordenar por CreatedAt e Id descendentes y aplicar Skip/Take. page=1 y pageSize=10 por defecto; máximo 100. Validación JSON 400 de enums, enteros, valores repetidos y desplazamiento fuera de int, calculado previamente en long. TotalPages usa aritmética long y vale 0 sin resultados. TicketPageDto devuelve items y metadatos; cada ticket conserva sus propiedades.

Cambio de contrato documentado: el listado deja de devolver un array. Los otros endpoints, reglas, modelos, contexto, esquema y migraciones permanecen intactos. No se añaden paquetes ni capas. Conteo y página son dos consultas y pueden observar escrituras concurrentes.

Añadidos 46 casos HTTP PostgreSQL en TicketEndpointsTests.List.cs: valores de filtros y combinación, búsqueda por ambos textos sin distinguir mayúsculas, texto literal con comodines/SQL, resultados vacíos, valores predeterminados, orden con empates, páginas consecutivas sin duplicados, totales, límite 100 y parámetros inválidos/desbordamientos. Reutilizan fixture/fábrica y limpieza limitada a títulos GUID e IDs propios. Los 81 casos anteriores conservan las regresiones de los demás endpoints.

Verificaciones reales: dotnet restore correcto; dotnet build --no-restore sin errores ni advertencias; dotnet test --no-build --filter 'Category!=PostgreSQL': 70 superadas, 0 fallidas, 0 omitidas. Listado de 127 casos PostgreSQL sin ejecución. Los primeros restore/build fallaron dentro del sandbox sin diagnóstico útil; se completaron fuera con autorización. HELPDESK_TEST_CONNECTION_STRING ausente en el proceso: no se ejecutó PostgreSQL, no se abrieron conexiones ni se aplicaron migraciones. Pendiente ejecutar los 127 casos con configuración segura para verificar comportamiento y limpieza real. git diff --check correcto. Sin commit ni push.

## Etapa 8: modelo de usuarios y roles — 21 de octubre de 2026

Implementados User (Id, Name, Email, Role y CreatedAt) y UserRole (User, Technician, Admin). DbSet Users y Fluent API con identity, NOT NULL y longitudes 100/254, índice único de Email, conversión de Role a texto con default User, y timestamp with time zone. CreatedAt inicial UTC; Npgsql exige UTC al persistir. Email se recorta y pasa a minúsculas invariantes al asignarlo; CHECK en PostgreSQL exige correo canónico. SQL directo debe normalizar previamente. No hay validación de sintaxis ni equivalencias Unicode/proveedor; revisar política regional para direcciones internacionalizadas. NOT NULL no sustituye la futura validación de cadenas vacías.

Generada 20261021113150_AddUsers, Designer y snapshot con EF 10.0.12 y configuración temporal Testing sin credenciales ni conexión. Revisados Up, Down y SQL incremental AddTicketCategory → AddUsers: únicamente CREATE TABLE Users con PK/CHECK, CREATE UNIQUE INDEX y registro de historial. Down solo elimina Users. No se toca Tickets ni se insertan usuarios ficticios. No se aplica la migración a ninguna base en esta sesión; desarrollo queda para revisión/aplicación manual.

Nueve casos nuevos sin base: roles/default UTC, normalización repetible, metadatos y conversión de roles, ausencia de relaciones y operaciones acotadas de la migración. Catorce nuevos PostgreSQL: persistencia de los tres roles con representación SQL y fecha UTC, límites exactos 100/254, default SQL User, duplicados exactos y por capitalización, actualización duplicada, null y longitudes excesivas, SQL no canónico y fecha no UTC. Los casos de persistencia conservan un ticket propio como control. Reutilizan la clase parcial, fixture/fábrica y validación existentes. Limpieza limitada a correos GUID exactos e IDs propios; tickets se limpian en finally.

Sin endpoints de usuarios, autenticación, contraseñas, relaciones, paquetes, cambios del contrato HTTP de tickets, commit ni push.

Verificaciones reales de cierre:

- dotnet restore HelpDesk.NET.slnx correcto; primer intento en sandbox terminó con código 1 sin diagnóstico, repetido fuera con autorización.
- dotnet build HelpDesk.NET.slnx --no-restore: 0 errores, 0 advertencias.
- dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL': 79 superadas, 0 fallidas y 0 omitidas (nueve nuevas).
- --list-tests --filter 'Category=PostgreSQL': 141 casos descubiertos (14 nuevos), sin ejecución ni conexiones.
- dotnet ef migrations has-pending-model-changes: no hay diferencias entre modelo y snapshot. SQL incremental generado y revisado, sin ejecutarlo.
- HELPDESK_TEST_CONNECTION_STRING ausente: no se verificó persistencia, restricciones ni limpieza contra PostgreSQL. Quedan pendientes los 141 casos con conexión segura; no se accedió a helpdesk_db ni se aplicaron migraciones.
- git diff --check correcto. Program.cs, Ticket y archivos de seguridad de pruebas sin cambios. Sin commit ni push.

Antes de aplicar manualmente: revisar el SQL y migraciones previas pendientes, confirmar que Users no existe por una creación ajena a EF, y aceptar la política de correo canónico. Si se requieren direcciones internacionalizadas, revisar capitalización regional de PostgreSQL frente a ToLowerInvariant. Revertir AddUsers eliminaría los usuarios almacenados.

## Etapa 8.2: endpoints REST de usuarios — 21 de octubre de 2026

Punto de partida comunicado por el desarrollador: etapa 8.1 guardada en GitHub, AddUsers aplicada a desarrollo y 220 pruebas superadas. Se distingue de las ejecuciones del agente documentadas anteriormente; esta sesión no verifica ni modifica desarrollo.

Implementados GET /api/users (array ordenado por Id ascendente), GET /api/users/{id}, POST /api/users (201 con Location) y PUT /api/users/{id}. DTOs separados de entidad y proyecciones AsNoTracking en GET. PUT conserva Id y CreatedAt; no existe DELETE de usuarios. Name obligatorio, máximo 100; Email normalizado Trim/ToLowerInvariant, máximo 254 y formato razonable mediante MailAddress más dominio ASCII con punto y etiquetas válidas. Role exacto: User/Technician/Admin; POST usa User si se omite, PUT exige explícitamente Role. Errores JSON/validación 400, inexistentes 404, duplicados 409. Se consulta la existencia del correo y se captura además 23505 exclusivamente del índice IX_Users_Email para carreras concurrentes.

Añadidas 32 pruebas sin base y 66 HTTP PostgreSQL: roles/default, validación, DTOs, consulta/orden, creación/actualización, límites exactos y excesivos, persistencia, protección de Id/CreatedAt, correos duplicados por capitalización, 404/400/409 y carreras POST/POST y PUT/PUT forzadas después de la consulta previa. Reutilizada la clase parcial, fixture y limpieza propia. Los tests previos permanecen como regresiones de tickets y persistencia; un nuevo caso también verifica un ticket propio como control.

Sin cambios en modelos, contexto, esquema, migraciones o dependencias; tampoco en handlers/contrato de tickets ni seguridad de pruebas. README incluye contrato y comandos curl manuales. Sin autenticación, JWT, contraseñas, relaciones, acceso a desarrollo, commit ni push.

Verificaciones reales de esta etapa:

- dotnet restore HelpDesk.NET.slnx: correcto. Primer intento en sandbox terminó con código 1 sin diagnóstico; se repitió fuera con autorización.
- dotnet build HelpDesk.NET.slnx --no-restore: correcto, 0 errores y 0 advertencias.
- dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL': 111 superadas, 0 fallidas y 0 omitidas (32 nuevas).
- --list-tests --filter 'Category=PostgreSQL': 207 casos descubiertos, 66 nuevos, sin ejecutar. Total definido: 318 casos.
- HELPDESK_TEST_CONNECTION_STRING ausente: ninguna conexión PostgreSQL ni prueba HTTP contra base ejecutada. Pendiente validar los 207 casos, especialmente carreras de unicidad y limpieza real, con conexión segura a helpdesk_test_db.
- Comparación automática con HEAD: handlers de tickets idénticos. Modelos, contexto, migraciones y archivos de seguridad de pruebas sin cambios.
- git diff --check correcto. Sin commit ni push; cambios pendientes de revisión del usuario.

Limitaciones actuales: listado de usuarios completo sin paginar; endpoints sin autenticación por el alcance de esta etapa. Validación de correo sintáctica, no comprueba entrega ni todas las equivalencias Unicode. No se generan ni aplican migraciones; no se ejecutan los curl del README porque escriben datos de la base elegida.

## Etapa 8.3: relaciones y asignación — 21 de octubre de 2026

El desarrollador comunica migraciones hasta AddUsers aplicadas y las 318 pruebas anteriores superadas; código 8.2 guardado en GitHub. Esta sesión parte de repositorio limpio y no consulta ni modifica desarrollo.

Ticket incorpora CreatedByUserId y AssignedToUserId anulables, con navegaciones excluidas de JSON y dos relaciones explícitas uno a muchos, índices y RESTRICT. POST admite creador opcional de cualquier rol, valida su existencia con 400 y conserva las creaciones anteriores sin creador. Las respuestas de tickets añaden los dos IDs; el listado sigue paginado con el mismo sobre. PUT continúa modificando solo los textos y los PATCH de clasificación conservan ambas relaciones.

PATCH /api/tickets/{id}/assignment asigna/reasigna a Technician o Admin, acepta null explícito para desasignar, devuelve 400 por ID/rol inválido y cuerpo incompleto, 404 por ticket inexistente y 200 con ticket actualizado. Repetir un destino válido no guarda. POST/PATCH convierten violaciones de las dos nuevas FK en 409 si desaparece el usuario tras validarlo. PUT de usuarios impide cambiar a User mientras existan asignaciones (409). No hay autenticación ni nuevos DELETE.

AddTicketUserRelations (20261021120235) generada con configuración temporal de diseño Testing, sin credenciales/conexión. Revisados Up, Down y SQL incremental: dos columnas int NULL, dos índices y dos FK RESTRICT; no hay operaciones destructivas en Up ni cambios de datos previos. No se aplica en ninguna base. Down pierde los dos IDs al retirar las columnas; el resto del ticket permanece. La API necesita el esquema migrado antes de utilizarse.

Nueve casos nuevos sin PostgreSQL (modelo, JSON/DTO y seguridad de operaciones de migración) y 38 nuevos de integración: creadores por rol y compatibilidad sin creador, errores, persistencia/navegaciones, asignación/reasignación/desasignación, campos protegidos, listado filtrado/paginado con tickets sin relaciones, FK y borrado restringido, degradación de rol y carreras controladas. Helpers compartidos comparan los nuevos IDs; limpieza ahora tickets propios antes que usuarios propios. Seguridad de fixture/fábrica/validador intacta.

Límite identificado al cerrar la implementación inicial (resuelto por la revisión siguiente): validaciones previas sin bloqueo común no garantizan rol técnico ante asignación y cambio de rol simultáneos. Un test reproduce conceptualmente esa carrera con pausa después de validar (compilado, pendiente de PostgreSQL). No se promete atomicidad ni se añade un trigger. PostgreSQL solo garantiza existencia y restricciones de borrado. Asignaciones simultáneas al mismo ticket usan última escritura.

Verificaciones reales: restore correcto tras fallo inicial del sandbox sin diagnóstico; build sin errores ni advertencias; 120 pruebas sin PostgreSQL superadas, 0 fallidas/omitidas. Descubiertos 245 casos PostgreSQL sin ejecutarlos; total definido 365. HELPDESK_TEST_CONNECTION_STRING ausente, ninguna conexión PostgreSQL ni aplicación de migraciones. Comprobación EF de snapshot sin cambios pendientes. git diff --check correcto. Pendiente ejecutar los 245 casos con conexión segura y comprobar limpieza real, después revisar/aplicar manualmente AddTicketUserRelations. Sin commit ni push.


## Etapa 8.3: concurrencia entre asignación y rol — 21 de octubre de 2026

El usuario comunica 245 pruebas PostgreSQL y 120 sin PostgreSQL superadas antes de esta revisión. Al iniciar esta sesión ya había cambios locales con transacciones, bloqueos y pruebas concurrentes; se conservaron y revisaron, sin atribuirlos a una ejecución de esta sesión.

La solución mantiene una transacción ReadCommitted hasta confirmar cada escritura: PATCH bloquea la fila del destino con FOR SHARE y valida su rol bajo ese bloqueo; PUT lee y bloquea el usuario con FOR NO KEY UPDATE antes de consultar sus asignaciones. Si PATCH confirma primero, PUT ve la asignación y devuelve 409. Si PUT confirma primero la degradación, PATCH lee User y devuelve 400. El orden de bloqueos de filas es usuario y después escritura del ticket; PUT no bloquea filas de tickets al consultar su existencia. Los bloqueos compartidos permiten asignaciones a tickets distintos del mismo técnico. Desasignar no abre una transacción explícita ni bloquea usuarios. Los retornos tempranos y errores liberan el bloqueo al disponer la transacción. Contratos y mensajes se conservan.

La suite concurrente contiene doce casos: ocho combinaciones de ambos órdenes, Technician/Admin y asignación/reasignación; dos casos de independencia entre usuarios; uno de asignaciones simultáneas a tickets distintos del mismo técnico; y uno de borrado bloqueado hasta commit y rechazado después por FK. Usa peticiones HTTP solapadas, interceptores y TaskCompletionSource; pg_blocking_pids confirma la espera real antes de liberar la primera operación. No usa Task.Delay; los límites temporales solo detectan fallos. Se verifica la persistencia mediante contextos independientes y se reutiliza la limpieza de datos propios.

Validación ejecutada en esta sesión: restore correcto; build correcto, cero errores y advertencias; 120 pruebas sin PostgreSQL superadas, cero fallidas u omitidas. Restore y build fallaron inicialmente en sandbox sin diagnóstico útil y se repitieron fuera con autorización. Descubrimiento de pruebas PostgreSQL completado sin ejecutar la fixture. HELPDESK_TEST_CONNECTION_STRING ausente: no se ejecutó ninguna prueba PostgreSQL ni se abrió conexión. La ejecución real de la nueva concurrencia queda pendiente; no se declara superada. git diff --check correcto.

Sin nuevas dependencias, capas, cambios de esquema o migraciones adicionales. No se aplicaron migraciones ni se accedió a desarrollo; sin commit ni push. La garantía corresponde a estas operaciones HTTP; no impone una restricción de rol a escrituras SQL externas. Asignaciones simultáneas sobre el mismo ticket siguen usando última escritura, fuera del alcance solicitado.


## Etapa 9.1: contraseñas y registro — 21 de octubre de 2026

Punto de partida: repositorio limpio. El usuario comunica 375 pruebas superadas previamente; ese resultado no se atribuye a esta sesión.

Implementados PasswordHash nullable/JsonIgnore en User, mapeo text NULL y POST /api/auth/register con RegisterUserDto. Reutiliza validación/normalización de nombre y correo, crea exclusivamente User, usa PasswordHasher<User> V3 con salt aleatorio y 210.000 iteraciones, y devuelve 201 con Location y DTO público. Política: 15–128 unidades UTF-16, no solo espacios ni controles, sin recortar/truncar/normalizar. Duplicados 409 incluidos registros concurrentes por índice único. Sin nuevos paquetes, tablas de Identity ni criptografía propia.

POST/PUT /api/users ahora devuelven siempre 403 en todos los entornos antes de acceder al contexto/cuerpo. Se retiran sus handlers públicos y pruebas de escritura anteriores, sustituyéndolos por comprobaciones de prohibición y conservación de datos; no hay skips ni bypass de Testing. GET y tickets permanecen, incluidos los bloqueos de assignment. Cuando vuelva la edición autorizada de roles deberá conservarse el protocolo 034.

Migración 20261021123515_AddUserPasswordHash generada con EF y configuración de diseño Testing sin credenciales/conexión; Designer y snapshot actualizados. Up añade solo Users.PasswordHash text nullable, sin default ni backfill, sin cambios de tickets/relaciones/datos. SQL incremental revisado: ALTER TABLE ADD y registro de historial dentro de transacción. Down pierde hashes al retirar la columna. No se aplicó ninguna migración en esta sesión ni se consultó/modificó desarrollo. Cuentas anteriores conservan NULL; no pueden reclamarse por registro y el futuro login deberá rechazarlas hasta activación segura. Procedimiento futuro y revisión previa documentados en AUTHENTICATION.md, sin implementarlos.

Añadidos 17 casos sin PostgreSQL y 39 PostgreSQL: registro válido/normalización, límites, errores de nombre/correo/contraseña/JSON, rol User pese a campos extra, hash distinto y verificable, salt, ausencia de hash/contraseña en DTOs y logs, 403 en todos los entornos, duplicados de cuentas anteriores y concurrentes, preservación de usuarios/tickets/relaciones. La prueba de migración aplica DDL real generado a tablas temporales con filas y FK anteriores, sin revertir el esquema compartido; pendiente de ejecución. Mantiene la barrera de concurrencia sin Task.Delay, fixture/fábrica base/validador y limpieza por filas propias.

Verificaciones finales de esta sesión:

- dotnet restore: correcto tras fallo en sandbox sin diagnóstico; repetido fuera con autorización.
- dotnet build --no-restore: correcto, cero errores y advertencias. El primer intento restringido falló sin diagnóstico; una compilación intermedia detectó un using ausente en una prueba y se corrigió antes de la validación final.
- dotnet test --no-build --filter 'Category!=PostgreSQL': 137 superadas, cero fallidas y omitidas.
- Descubrimiento PostgreSQL: 217 casos (34 de registro, cuatro de gestión prohibida, uno de migración, más regresiones conservadas), sin ejecutar fixture ni conexiones. Total actual 354; la variación respecto a 375 se debe a sustituir los antiguos contratos de escritura pública, no a omitir pruebas.
- HELPDESK_TEST_CONNECTION_STRING ausente: cero pruebas PostgreSQL ejecutadas; no se declaran superadas ni se acredita aún persistencia real del registro o la migración.
- SQL generado/revisado y has-pending-model-changes: modelo/snapshot coinciden, sin conectar.
- git diff --check correcto. README, ARCHITECTURE y guías actualizados. Sin commit ni push; pendiente de revisión del usuario y ejecución segura PostgreSQL antes de aplicar manualmente a desarrollo.


## Etapa 9.2: login y JWT — 23 de octubre de 2026

Al iniciar ya existían cambios locales de login, configuración JWT y pruebas. Se conservaron y revisaron; se corrigieron comprobaciones de fechas con una propiedad obsoleta y tipos incompatibles, y se aisló el rechazo de algoritmo usando la misma clave de confianza.

Implementado POST /api/auth/login: campos obligatorios, correo normalizado como en registro, consulta PostgreSQL AsNoTracking, verificación por PasswordHasher<User> y 401 genérico para cuenta ausente, contraseña incorrecta o hash NULL/vacío/corrupto. No escribe datos. Responde únicamente accessToken, tokenType Bearer y expiresIn 1800 con no-store. Emite HS256 y claims sub/email/role/iss/aud/iat/exp/nbf; rol tomado del usuario almacenado.

JwtSettings valida configuración al inicio; clave Base64 externa de al menos 32 bytes generados criptográficamente, sin defaults ni regeneración automática. JwtTokenIssuer usa TimeProvider. Dependencia nueva: Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12. Handler estándar con validación de firma HS256, issuer, audience, caducidad y margen de 30 segundos; middleware de autenticación sin autorización nueva. Manejador de excepciones con respuesta genérica también en Development. Registro, política de contraseñas, gestión de usuarios bloqueada y permisos de tickets conservados.

Archivos nuevos: Authentication/JwtSettings.cs, Authentication/JwtTokenIssuer.cs, DTOs/LoginDto.cs y DTOs/LoginResponseDto.cs en HelpDesk.Api; JwtTestSettings.cs, JwtSecurityTests.cs e Integration/TicketEndpointsTests.Login.cs en HelpDesk.Tests. Modificados Program.cs y csproj de API; HealthEndpointTests, RegistrationSecurityTests y TicketApiFactory para opciones efímeras de tests. Actualizados README, AUTHENTICATION, ARCHITECTURE y este registro.

Verificaciones ejecutadas:

- dotnet restore HelpDesk.NET.slnx: correcto. Primer intento restringido terminó con código 1 sin diagnóstico; repetido fuera del sandbox con autorización.
- dotnet build HelpDesk.NET.slnx --no-restore: correcto, cero errores y advertencias. El intento restringido falló sin diagnóstico; primera compilación fuera detectó dos errores de tipos y dos avisos de Iat obsoleto en pruebas; corregidos antes del resultado final.
- dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL': 155 superadas, cero fallidas u omitidas. El intento restringido falló por SocketException/Permission denied de MSBuild; repetido fuera con autorización.
- Descubrimiento mediante --list-tests --filter 'Category=PostgreSQL': 231 casos, sin ejecución de fixture ni conexiones. Total 386 (18 casos nuevos sin base y 14 con PostgreSQL).
- HELPDESK_TEST_CONNECTION_STRING ausente: ninguna prueba PostgreSQL ejecutada ni conexión a desarrollo. La integración HTTP de login y sus regresiones de persistencia/privacidad quedan pendientes; no se declaran superadas.
- git diff --check correcto al cierre. Sin cambios en modelos, configuración versionada de secretos, migraciones ni datos de desarrollo; sin commit ni push.

La guía Linux Mint genera una clave una sola vez y la guarda por stdin en User Secrets sin mostrarla; el cliente manual solicita contraseña oculta y no imprime el token. No se ejecutaron esos ejemplos ni se cambió la configuración local del usuario. JWT no cifra claims ni se revoca automáticamente por cambios posteriores de rol/estado. No hay refresh tokens ni limitación de intentos; persiste una diferencia temporal entre cuenta ausente y contraseña errónea. Autorización y revocación se diseñarán tras revisión del usuario; no se continúa con 9.3.


## Etapa 9.3: autorización por roles y recursos — 23 de octubre de 2026

Punto de partida limpio. El usuario comunica etapa 9.2 probada y subida a GitHub; ese resultado no se atribuye a esta sesión. Revisados instrucciones, endpoints, DTOs, modelos, JWT, documentación e infraestructura de pruebas antes de implementar.

AddAuthorization y UseAuthorization incorporados. Solo health, registro y login son públicos. DefaultPolicy/FallbackPolicy y políticas Admin/ChangeStatus exigen cuenta actual mediante CurrentAccountRequirement; su handler scoped consulta existencia/rol en PostgreSQL por el sub del JWT autenticado y exige coincidencia con el claim. No se confía en cuerpos ni cabeceras alternativas. JWT ausente/inválido/caducado produce 401; cuenta ausente/rol cambiado, 403 genérico. No hay caché de roles entre peticiones ni fallback de privilegios si falla PostgreSQL.

TicketAccess compone el filtro SQL antes de count/filtros/paginación: User propios, Technician propios o asignados, Admin todos. Detalle invisible 404. Históricos sin creador quedan solo para Admin o técnico asignado. POST siempre asigna creador autenticado e ignora cualquier CreatedByUserId del cliente. Gestión de textos, clasificación, asignación y borrado solo Admin. Estado permite además Technician asignado, leyendo con FOR UPDATE dentro de ReadCommitted antes de comprobar el recurso y conservando el bloqueo hasta finalizar.

GET de usuarios reservado a Admin y perfil propio seguro en GET por ID, con 404 ajeno. POST/PUT administrativos reactivados tras revisión: validación de correo/rol, unicidad y DTO sin hash; POST crea sin credencial, PUT conserva hash/ID/fecha. PUT mantiene FOR NO KEY UPDATE antes de comprobar asignaciones y assignment FOR SHARE hasta commit; conversión a User con asignaciones devuelve 409. No se crea ni activa un Admin, ni se modifica una cuenta existente para obtenerlo. Si no existe un Admin con contraseña válida, administración inaccesible deliberadamente; aprovisionamiento inicial seguro propuesto para otra etapa, sin bypass implementado.

Archivos creados:

- HelpDesk.Api/Authorization/AccessPolicies.cs: políticas, requisito/handler de cuenta actual y estado scoped.
- HelpDesk.Api/Authorization/TicketAccess.cs: filtro de visibilidad y permiso de estado centralizados.
- HelpDesk.Tests/AuthorizationEndpointTests.cs: autenticación obligatoria, metadatos y claims inválidos sin base.
- HelpDesk.Tests/Integration/TicketEndpointsTests.Authentication.cs: clientes con cuentas propias y JWT reales.
- HelpDesk.Tests/Integration/TicketEndpointsTests.Authorization.cs: permisos por rol/recurso, históricos, creador, perfil, identidad obsoleta, administración y unicidad concurrente.
- HelpDesk.Tests/Integration/TicketEndpointsTests.RoleConcurrency.cs: ocho combinaciones de cambio de rol/asignación y carrera de reasignación/estado.

Archivos modificados: HelpDesk.Api/Program.cs; HelpDesk.Tests/RegistrationSecurityTests.cs; Integration/TicketEndpointsTests.cs y sus parciales AssignmentConcurrency, Category, List, Login, Priority, Registration, Relations y UserHttp; README y guías AUTHENTICATION, ARCHITECTURE, POSTGRESQL_TESTS y este registro. Sin cambios en csproj, modelos, DTOs, migraciones, fixture ni validador de conexión. No hay dependencias nuevas.

Las regresiones HTTP de reglas y persistencia se autentican como Admin con una cuenta propia, sin sustituir el esquema Bearer ni omitir políticas. Se mantienen registro/login anónimos, validación, integridad, filtros, limpieza y bloqueos. Se adaptan expectativas de creador y gestión anónima al nuevo contrato. Añadidos 61 casos sin PostgreSQL y 42 PostgreSQL. Los casos nuevos de permisos recorren User/Technician/Admin y negaciones; las carreras observan pg_blocking_pids, no usan retrasos como sincronización.

Verificaciones reales finales:

- dotnet restore HelpDesk.NET.slnx: correcto. Primer intento restringido terminó con código 1 sin diagnóstico; completado fuera con autorización.
- dotnet build HelpDesk.NET.slnx --no-restore: correcto, cero errores/advertencias. El intento restringido falló sin diagnóstico; compilación realizada fuera con autorización.
- dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL': 216 superadas, cero fallidas/omitidas. El intento restringido falló por SocketException/Permission denied de MSBuild; completado fuera con autorización. Una ejecución intermedia detectó que el lector JWT rechaza sub duplicado con 401 antes de la política; se corrigió la expectativa de esa prueba y se repitió la suite completa satisfactoriamente.
- Descubrimiento con --list-tests --filter 'Category=PostgreSQL': 273 casos, sin ejecutar fixture ni abrir conexiones. Total definido: 489. Descubrir no valida permisos, persistencia ni bloqueos en PostgreSQL.
- HELPDESK_TEST_CONNECTION_STRING ausente: cero pruebas PostgreSQL ejecutadas; no se consultó ni modificó desarrollo ni se aplicaron migraciones. La ejecución segura de los 273 casos queda pendiente.
- Documentación revisada y snippets Python de AUTHENTICATION.md comprobados sintácticamente sin ejecutarlos. git diff --check correcto al cierre; archivos nuevos revisados para espacios finales. Sin commit ni push.

Límites: el rol se comprueba al inicio de cada petición protegida; no se cancelan peticiones ya autorizadas. Cambiar el rol invalida la autorización del token mientras no coincida, pero no revoca permanentemente una sesión si el rol vuelve al anterior. No hay revocación por contraseña, deshabilitación, limitación de intentos, activación o protección ante auto degradar al último Admin. JWT no cifra sus claims. Se conserva la limitación de última escritura para gestión administrativa concurrente y las reglas de rol no protegen SQL externo. Detalle y cliente manual por cada rol de solo lectura en AUTHENTICATION.md. No se continúa con etapas adicionales sin aprobación.


## Etapa 9.4: inicialización segura del primer Admin — 23 de octubre de 2026

Punto de partida limpio. El usuario comunica etapas anteriores probadas y subidas a GitHub, ausencia de Admin operativo y seis tickets/dos usuarios de desarrollo que se deben conservar. Esta sesión no verifica ni modifica esas filas. Revisados Program, modelos, DbContext, JWT/políticas, validadores, configuración externa/UserSecretsId, bloqueo de asignación/cambio de rol y pruebas; no se leen valores de User Secrets.

Implementado HelpDesk.AdminBootstrap, consola net10.0 separada de HTTP y añadida a la solución. Reutiliza modelos/contexto/validación mediante referencia a API; esta no referencia ni inicia la consola. Configuración de PasswordHasher<User> extraída a AddHelpDeskPasswordHashing, común a API y CLI: Identity V3, 210.000 iteraciones, sin cambios de política ni endpoints.

La CLI exige terminal interactiva sin redirección y no acepta argumentos con credenciales. Lee únicamente HELPDESK_BOOTSTRAP_CONNECTION_STRING externa y filtra sus opciones, fija public y oculta errores del proveedor; no carga JWT, appsettings, User Secrets ni DefaultConnection. Muestra host/puerto/base/usuario y operación; el operador debe escribir el nombre exacto de base, email nuevo, nombre, contraseña oculta dos veces y CONFIRMAR PRIMER ADMIN. Cancelaciones/validación errónea no abren conexiones. Verifica también database/user efectivos antes de escribir.

BootstrapService calcula el hash fuera del bloqueo y usa ReadCommitted con LOCK TABLE public.Users IN SHARE ROW EXCLUSIVE MODE antes de consultar Admin/correo e insertar. El modo es autoconflictivo y excluye ROW EXCLUSIVE de escrituras, incluso de otros programas. Dos procesos se serializan: el segundo espera y ve el Admin confirmado por el primero en su consulta posterior. Rechaza cualquier Admin existente, incluso con hash NULL, y cualquier correo ocupado, sin promoción/activación ni cambios de contraseña. Solo INSERT de la cuenta nueva; sin cambios de Tickets, filas anteriores, roles existentes, migraciones ni indicadores persistentes. La condición es ausencia actual de Admin, no un registro de bootstrap irreversible.

Archivos creados:

- HelpDesk.AdminBootstrap/HelpDesk.AdminBootstrap.csproj.
- HelpDesk.AdminBootstrap/BootstrapProgram.cs: punto de entrada sin host HTTP ni migraciones.
- HelpDesk.AdminBootstrap/BootstrapConnection.cs: conexión dedicada validada y metadatos de destino sin exponer secretos.
- HelpDesk.AdminBootstrap/BootstrapConsole.cs: lectura interactiva y contraseña sin eco.
- HelpDesk.AdminBootstrap/BootstrapCommand.cs: flujo, confirmaciones, códigos de salida y errores seguros.
- HelpDesk.AdminBootstrap/BootstrapService.cs: validación defensiva y transacción del primer Admin.
- HelpDesk.Api/Authentication/PasswordHashing.cs: configuración compartida del hasher.
- HelpDesk.Tests/BootstrapSecurityTests.cs: 41 casos sin PostgreSQL, incluida cancelación en CLI real.
- HelpDesk.Tests/Integration/BootstrapTerminalProcess.cs: PTY Linux con script/stty, sin archivos de transcripción ni secretos en argumentos.
- HelpDesk.Tests/Integration/TicketEndpointsTests.Bootstrap.cs: siete casos PostgreSQL con fixture/limpieza existentes.

Archivos modificados: HelpDesk.Api/Program.cs (solo extraer registro del hasher), HelpDesk.NET.slnx, HelpDesk.Tests/HelpDesk.Tests.csproj y README, AUTHENTICATION, ARCHITECTURE, POSTGRESQL_TESTS y este registro. No hay dependencias NuGet nuevas; solo referencias de proyecto. Modelos, DTOs, políticas, migraciones, fixture y validador de destino de pruebas intactos.

Pruebas nuevas de entrada/confirmación, hasher común, opciones de conexión y no exposición de secretos. Integración preparada para creación/login/JWT/Admin, Admin existente con/sin contraseña, correo User/Technician duplicado, cancelación y dos procesos CLI concurrentes bajo PTY. Se preparan dos usuarios y seis tickets propios y se comparan campos/hashes/relaciones. Un bloqueo del test y pg_locks demuestran la espera simultánea antes de liberar a los dos procesos; no se usa un retraso para decidir el orden. Esas pruebas PostgreSQL no se han ejecutado. La clase parcial serializa su ejecución con las regresiones; si hay Admin ajenos no se borran ni degradan para preparar pruebas, se falla explícitamente. Limpieza por GUID/IDs propios heredada.

Verificaciones reales:

- dotnet restore HelpDesk.NET.slnx: correcto fuera del sandbox con autorización; intentos restringidos terminaron con código 1 sin diagnóstico.
- dotnet build HelpDesk.NET.slnx --no-restore: correcto, cero errores y advertencias. Intento restringido falló sin diagnóstico, repetido fuera con autorización.
- dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL': 257 superadas, cero fallidas/omitidas. El intento restringido falló por sockets internos de MSBuild (Permission denied); ejecutado fuera con autorización. Primera suite de 256 ya pasó y se repitió tras añadir la comprobación de terminal real.
- La prueba real de PTY ejecutó el binario con conexión ficticia a helpdesk_test_db, contraseña efímera y confirmación incorrecta: salida 2 y sin mostrar secretos; cancelación antes de abrir conexión. No se creó ninguna cuenta ni se utilizó desarrollo.
- Descubrimiento --list-tests --filter 'Category=PostgreSQL': 280 casos, sin fixture ni conexiones. Total definido 537: 257 sin base y 280 con PostgreSQL. Descubrir no valida creación, login ni concurrencia PostgreSQL.
- HELPDESK_TEST_CONNECTION_STRING ausente: cero casos PostgreSQL ejecutados y ningún bootstrap contra una base. No se aplicaron migraciones ni se consultó/modificó desarrollo.
- Ejemplos Python de AUTHENTICATION.md comprobados sintácticamente sin ejecutarlos. git diff --check correcto al cierre; revisados archivos nuevos y diferencia de configuración/solución.

README y guías incluyen carga de conexión oculta, frase exacta, verificación Admin mediante login/GET sin alterar tickets, rechazo cuando existe Admin y limitación ante contraseña perdida. No hay recuperación ni activación de cuentas antiguas; no se propone borrar/degradar Admin ni editar hashes para reabrir el bootstrap. El rol PostgreSQL debe tener permisos legítimos; el bloqueo puede retrasar escrituras de Users, por lo que requiere ventana coordinada. Una pérdida de respuesta durante commit exige verificar el resultado antes de reintentar. No se promete borrado criptográfico de strings de memoria; entorno/terminal/equipo deben protegerse.

Sin commit ni push. Creación del primer Admin real pendiente de revisión y autorización explícita del usuario; no se solicita ni se ejecuta en esta entrega. No se continúa con otras etapas.

## Revisión de tres fallos PostgreSQL — 23 de octubre de 2026

El usuario comunica 280 ejecutadas, 277 superadas y tres fallidas. Al iniciar esta revisión ya había correcciones locales para los tres fallos y una regresión del orden inverso; se conservan y revisan junto con endpoints, transacciones y filtros de autorización.

- Fechas: AssertTicketEqual y el caso administrativo comparaban ticks completos de POST en memoria con lecturas persistidas. DateTime tiene resolución de 100 ns; timestamp with time zone conserva microsegundos y Npgsql descarta la fracción submicrosegundo al escribir. Las correcciones comparan exactamente los cocientes de ticks por TimeSpan.TicksPerMicrosecond y exigen UTC, conservando los demás campos. Sin modificar fechas, esquema ni migraciones.
- Concurrencia: PauseRelatedSave pausaba en SavingChangesAsync, antes del UPDATE de Tickets. El SHARE de Users no bloquea el FOR UPDATE de Tickets: el técnico podía terminar sin esperar y AssertBlockedByAsync agotaba diez segundos buscando un bloqueo inexistente. PauseTicketAfterSave pausa en SavedChangesAsync, después del SQL y antes del commit explícito, y exige CurrentTransaction. Se mantiene pg_blocking_pids y se exige una petición pendiente antes de liberar la pausa. Tras commit se exige 404 sin cuerpo y ticket íntegro con nuevo asignado y estado original.
- Regresión adicional ya presente: estado primero/reasignación después. Comprueba bloqueo real, ambas respuestas 200, estado/asignación finales y rechazo posterior del antiguo técnico. finally libera siempre las barreras. Sin aumentar timeouts ni omitir pruebas.

La inspección no justifica cambiar endpoints: status usa FOR UPDATE en ReadCommitted y comprueba el recurso antes de escribir. Assignment protege Users y después escribe Tickets; status bloquea Tickets sin solicitar después un bloqueo de Users. No aparece un ciclo entre ambas operaciones. La ausencia de interbloqueos y el comportamiento real de la consulta compuesta con VisibleTo requieren todavía ejecución PostgreSQL.

Archivos de correcciones: Integration/TicketEndpointsTests.cs, TicketEndpointsTests.Authorization.cs y TicketEndpointsTests.RoleConcurrency.cs. Esta revisión explicita la constante de precisión y añade cuerpo vacío al rechazo concurrente. Documentación actualizada en PROGRESS.md, ARCHITECTURE.md y POSTGRESQL_TESTS.md. Los demás cambios locales anteriores se conservan sin ampliarlos.

Validación realmente ejecutada:

- Restore correcto; build correcto con cero errores/advertencias, ambos fuera del sandbox. Intentos restringidos terminaron con código 1 sin diagnóstico.
- dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL': 257 superadas, cero fallidas/omitidas, fuera del sandbox. Intento restringido bloqueado por SocketException/Permission denied de MSBuild.
- Descubrimiento --list-tests --filter 'Category=PostgreSQL': 281 casos; no ejecuta fixture ni conecta.
- HELPDESK_TEST_CONNECTION_STRING ausente: cero pruebas PostgreSQL ejecutadas en esta revisión. Sin conexión a desarrollo, migraciones ni modificaciones de sus seis tickets o usuarios; no se ha consultado su contenido.
- git diff --check correcto. Sin commit ni push, activación del administrador ni otras etapas.

Pendiente: suite PostgreSQL corregida y repetición de concurrencia exclusivamente en helpdesk_test_db según POSTGRESQL_TESTS.md. La etapa 9.4 sigue pendiente de cierre.

## Cierre funcional de la etapa 9.4 — 27 de octubre de 2026

Resultados manuales confirmados por el usuario, sin repetir escrituras durante este cierre documental:

- Primer administrador creado mediante la consola independiente; cuenta ID 3 con correo local esperado y rol Admin.
- Contraseña recuperada correctamente mediante una herramienta temporal fuera del repositorio. No se añade un comando, endpoint ni proceso permanente de recuperación al proyecto.
- Tres usuarios y seis tickets conservados.
- API iniciada en Development; login del administrador HTTP 200.
- `GET /api/users` sin token HTTP 401; con JWT del administrador HTTP 200, incluyendo la cuenta ID 3 con el rol esperado.
- Verificación administrativa superada; etapa 9.4 completada funcionalmente.

La revisión del código confirma las validaciones, el hasher compartido y la protección Admin del directorio. La CLI de bootstrap no restablece contraseñas ni modifica cuentas existentes: rechaza cualquier Admin previo, incluso sin credencial. La intervención temporal de recuperación no cambia esa decisión.

Este cierre solo modifica documentación; no ejecuta bootstrap, recuperación, migraciones ni escrituras de datos. La ejecución posterior de la suite PostgreSQL corregida está confirmada por el usuario: 281 superadas, 0 fallidas.


Comprobaciones ejecutadas para este cierre documental:

- `dotnet build HelpDesk.NET.slnx -p:NuGetAudit=false --nologo`: correcto, cero errores y advertencias. La consulta de vulnerabilidades NuGet se desactivó para esta compilación; no se declara realizada.
- `dotnet test HelpDesk.NET.slnx --no-build --no-restore --filter 'Category!=PostgreSQL' --nologo`: 257 superadas, cero fallidas y cero omitidas.
- Los intentos restringidos fallaron por las limitaciones del sandbox (build sin diagnóstico; tests con permiso denegado al crear sockets de MSBuild). Ambas comprobaciones se completaron fuera del sandbox con autorización.
- Revisión de documentación frente a BootstrapProgram, BootstrapCommand, BootstrapService, PasswordValidation y las políticas/endpoints existentes. Procedimiento de primer Admin y política de 15–128 unidades UTF-16 coherentes con el código.
- `git diff --check` correcto; solo cambios en PROGRESS.md, ARCHITECTURE.md y AUTHENTICATION.md. Sin ejecución de tests PostgreSQL, migraciones, escrituras de datos, commit ni push.


## Confirmación final de pruebas — etapa 9.4

Resultados comunicados y confirmados por el usuario tras ejecutar ambas suites:

| Comprobación | Resultado |
|---|---|
| Pruebas sin PostgreSQL | 257 superadas, 0 fallidas |
| Pruebas PostgreSQL | 281 superadas, 0 fallidas |
| Total | **538 superadas, 0 fallidas** |
| Build anterior | 0 errores, 0 advertencias |
| Login del administrador | HTTP 200 |
| Acceso administrativo autenticado | HTTP 200 |
| Auditoría de aislamiento PostgreSQL | Superada |

La etapa 9.4 queda completada con ambas suites superadas. Las menciones históricas a ejecución PostgreSQL pendiente ya no representan el estado vigente. Se mantiene el procedimiento de creación del primer Admin en AUTHENTICATION.md, incluida la política de 15–128 unidades UTF-16.

Esta actualización solo registra los resultados confirmados; no vuelve a ejecutar pruebas, migraciones ni operaciones de base de datos. Verificación documental mediante `git diff --check` y revisión de archivos modificados; sin commit ni push.


## Fase 10.1 — base Angular y conexión HTTP (27 de octubre de 2026)

- Proyecto generado con Angular CLI 22.2.4; dependencias instaladas con Node 24.15.0 y npm 11.12.1. TypeScript 6, Vitest y lockfile versionado, sin CLI global ni dependencias visuales adicionales.
- Organización mínima por funcionalidad: `layout`, `features/home` y `core/api`. Presentación separada en `HomeIntro`; servicio health inyectado y estado de la página mediante signals.
- Navegación Router, título de documento, idioma español, enlace para saltar al contenido, foco visible, estados anunciados con `role="status"`, iconos decorativos ocultos al lector y respeto de movimiento reducido.
- SCSS con tokens globales y estilos locales; layout adaptable para escritorio y móvil, sin métricas ni incidencias ficticias.
- Documentación de arranque y proxy en README. Exclusiones de node_modules, dist, caché Angular y cobertura; `package-lock.json` conservado.

Validaciones realizadas:

- `npm run build`: correcto, sin advertencias en la versión final. Un aviso inicial de presupuesto SCSS se resolvió separando la presentación en su componente, sin aumentar los presupuestos.
- `npm test -- --watch=false`: 14 pruebas superadas, cero fallidas. HTTP simulado; cubren navegación, carga, éxito, error, reintento, respuesta inválida, timeout y cancelación.
- API existente: GET `/api/health` correcto. Angular `/`: HTTP 200. Proxy real `http://localhost:4200/api/health`: HTTP 200 y contrato esperado. Solo peticiones de lectura, sin consultas a PostgreSQL.
- Revisión visual con Firefox headless a 1440 × 1100 y 390 × 1200; layout de escritorio y móvil con conexión correcta.
- `dotnet build HelpDesk.NET.slnx --no-restore --nologo`: correcto, cero errores y advertencias. `dotnet test HelpDesk.NET.slnx --no-build --no-restore --filter 'Category!=PostgreSQL' --nologo`: 257 superadas, cero fallidas/omitidas. Sin ejecutar las pruebas PostgreSQL ni migraciones.
- El intento inicial de generación Angular quedó sin salida dentro del sandbox; se canceló antes de crear archivos y se completó fuera de él. El sandbox también bloqueó sockets del servidor Angular y el build de MSBuild; repetidos fuera con autorización. Firefox necesitó crear previamente sus perfiles temporales en `/tmp`. Ninguno de estos diagnósticos cambió configuración de producción.

`git diff --check` correcto, incluida revisión de whitespace de los 34 archivos nuevos. Comprobado que node_modules, dist, caché y cobertura están excluidos y que package-lock.json no está ignorado. `git diff --exit-code` sobre los tres proyectos .NET y la solución confirma que no han cambiado.

La fase 10.1 añade únicamente el cliente, documentación y exclusiones de archivos generados; no modifica datos, endpoints ni autenticación. Sin commit ni push.


## Fase 10.2 — autenticación y sesiones Angular (27 de octubre de 2026)

Revisión previa de AGENTS.md, documentación, contratos y proxy, con repositorio limpio. Login usa email/password y respuesta accessToken/tokenType/expiresIn. Registro usa name/email/password, responde usuario público con 201 y fija User en servidor. Ningún endpoint nuevo.

- Nuevos archivos en `core/auth` para contratos, servicio de sesión, interceptor y guards; páginas en `features/auth` y menú de sesión en `layout`.
- Contraseñas ocultas en inputs, autocomplete apropiado, etiquetas y errores accesibles; política de registro 15–128 unidades UTF-16 sin recortar, normalizar ni truncar. Login no fuerza esa longitud.
- Token solo en memoria, sin logs o secretos en configuración. Los claims se leen para UX, no se verifican criptográficamente en el navegador. La expiración utiliza el límite menor de exp y expiresIn; al leer la sesión vuelve a comprobarse el reloj para cubrir pestañas suspendidas. Se admite para nbf la misma tolerancia inicial de 30 segundos del backend, sin prolongar exp.
- Interceptor limitado a URLs relativas protegidas, con normalización de ruta y exclusión de separadores codificados; no envía tokens a servicios externos ni a endpoints públicos. Redirecciones desactivadas para credenciales y peticiones Bearer.
- Logout local, 401 autenticado y caducidad limpian estado. Versionado de sesión evita carreras entre login/logout y respuestas HTTP tardías.
- Registro no selecciona rol ni inicia sesión automáticamente. `/session` muestra identidad, rol y vencimiento; los guards por rol quedan preparados para futuras rutas, sin paneles simulados.
- Pruebas nuevas con HttpTestingController, fixtures exclusivas de tests y relojes controlados. Ninguna respuesta o cuenta ficticia se incorpora al funcionamiento de producción. Los archivos de pruebas existentes se conservan intactos.

Validaciones realizadas:

- `npm run build`: correcto, sin advertencias. No se añaden dependencias ni se modifica package-lock.json.
- `npm test -- --watch=false`: **105 pruebas superadas, 0 fallidas**, en 10 archivos; las 14 anteriores siguen intactas. Las nuevas cubren contratos reales, formularios, mensajes seguros, roles, interceptor, sesión en memoria, caducidad, cancelación, condiciones de carrera, navegación privada y logout desde el menú real. Fixtures excluidas del código de aplicación; sin cuentas o respuestas simuladas en producción.
- `dotnet build HelpDesk.NET.slnx --no-restore --nologo`: correcto, 0 errores y 0 advertencias.
- `dotnet test HelpDesk.NET.slnx --no-build --no-restore --filter 'Category!=PostgreSQL' --nologo`: 257 superadas, 0 fallidas/omitidas. Sin pruebas PostgreSQL ni migraciones.
- Revisión visual con Firefox headless: login en 1440 × 1100 y registro en 390 × 1250, sin introducir credenciales ni enviar formularios.
- El sandbox bloqueó el build .NET sin diagnóstico y el puerto Angular con EPERM; se completaron las comprobaciones necesarias fuera del sandbox con autorización. El puerto 4200 ya estaba ocupado, así que la revisión visual utilizó temporalmente 4201 sin cambiar la configuración ni detener el servidor existente.
- `git diff --check` y revisión de whitespace de archivos nuevos correctos. `git diff --exit-code` confirma que los proyectos .NET, su solución, sus pruebas y los archivos de tests Angular anteriores no cambian. Revisión de TypeScript sin uso de localStorage, sessionStorage, document.cookie o logs de aplicación.

La verificación manual de login con credenciales reales y el registro válido no se ejecutan para respetar la base de datos; pasos en README. El registro válido debe comprobarse únicamente con una API configurada para una base aislada. No se modifican datos durante la implementación. Sin commit ni push.


## Fase 10.3.1 — listado de tickets (27 de octubre de 2026)

Revisión previa con repositorio limpio, contratos de Program.cs, TicketPageDto y Ticket, enums y TicketAccess. GET /api/tickets admite search/status/priority/category/page/pageSize. Orden fijo por CreatedAt e Id descendentes; no admite ordenar por parámetros. Respuesta paginada con items/totalCount/page/pageSize/totalPages y IDs de relaciones nullable, sin nombres de usuarios.

- Ruta privada `/tickets` con el authGuard existente, carga bajo demanda y consultas relativas con HttpClient. El interceptor JWT actual ya cubre la ruta; no se cambia.
- Tipos y comprobación de la respuesta real antes de mostrar filas. Tabla de escritorio y tarjetas en móvil; datos y totales únicamente de la API. Técnico identificado por ID o Sin asignar; fechas locales.
- Búsqueda literal con debounce de 350 ms, filtros por enums admitidos, tamaño 1–100 y paginación. Los cambios de filtros/tamaño reinician la página. No se añaden filtros de usuario/asignación ni ordenación no soportados.
- URL como fuente de la consulta, normalización de entradas inválidas y restauración al navegar por el historial. Un único switchMap cancela HTTP y debounce al cambiar cualquier criterio, navegar o salir; peticiones con timeout de 15 segundos y sin caché.
- Estados explícitos de carga, error, lista vacía, ausencia de coincidencias y página fuera de rango, con reintento o recuperación. Los errores no reflejan cuerpos del proveedor; 401 sigue delegado a la autenticación existente.
- Nuevas pruebas con HTTP simulado y Router real, sin usar cuentas reales ni acceder a PostgreSQL. Las fixtures no se incorporan al código de producción.

Validaciones realizadas:

- `npm run build`: correcto, sin advertencias.
- `npm test -- --watch=false`: **162 superadas, 0 fallidas**, en 15 archivos. Se conservan las 105 pruebas anteriores; solo se actualiza la expectativa obsoleta sobre el enlace a tickets, con autorización del usuario. Nuevas pruebas de servicio, consulta URL, interacciones, tabla segura y Router real: filtros, búsqueda/debounce, historial, paginación, validación de respuesta, timeout, error, vacío, reintento, cancelación, recuperación de páginas y acceso por los tres roles.
- Los tres fallos iniciales correspondían a tests nuevos: un caso construía una respuesta válida mientras esperaba rechazo y el doble de Router no serializaba los números como strings. Se corrigieron las fixtures/doble y se añadieron casos con Router real; suite final superada.
- `dotnet build HelpDesk.NET.slnx --no-restore --nologo`: correcto, 0 errores y 0 advertencias. El intento dentro del sandbox falló sin diagnóstico; repetido fuera con autorización.
- `dotnet test HelpDesk.NET.slnx --no-build --no-restore --filter 'Category!=PostgreSQL' --nologo`: 257 superadas, 0 fallidas/omitidas. No se ejecutaron las pruebas PostgreSQL.
- `git diff --check` y revisión de whitespace de los 15 archivos nuevos correctos. Backend, solución, autenticación, proxy y dependencias intactos. Nueve de los diez archivos de pruebas Angular anteriores no cambian; en session-menu.spec.ts solo cambia una aserción obsoleta, autorizada por el usuario.

Cierre de navegación: enlace autenticado añadido con los mismos estilos e iconografía de la barra lateral. Pruebas con Router real verifican el clic, la clase active y aria-current con y sin filtros, su desactivación al salir y la ausencia del enlace sin sesión. El guard redirige al login antes de consultar tickets. Build Angular y suite completa repetidos: sin advertencias y 162 pruebas superadas.

Limitaciones: No se verificó visualmente un listado autenticado con datos reales ni se manejaron credenciales: el procedimiento manual queda para una cuenta existente, sin crear datos para preparar la prueba. Ordenación fija y técnico por ID porque así lo ofrece la API. Sin migraciones, escrituras de datos, commit ni push.


## Fase 10.3.2 — creación y detalle (27 de octubre de 2026)

Revisión previa de AGENTS.md, documentación, repositorio limpio, DTOs y endpoints. POST /api/tickets permite cualquier cuenta autenticada, valida título obligatorio/máximo 100 unidades UTF-16, descripción obligatoria y enums; ignora creador enviado y responde 201 con Ticket y Location. GET /api/tickets/{id} devuelve solo una incidencia visible o 404; no distingue inexistencia de falta de visibilidad. No hay fecha de modificación ni nombres de usuarios en la respuesta.

- Formulario reactivo en features/tickets/create, con defaults Medium/Other y sin campos de creador, estado, fecha o asignación. Valida espacios de .NET y longitud UTF-16 sin transformar los textos ni añadir límite a descripción.
- Métodos create/get en TicketService, compartiendo la validación del contrato con el listado. El POST envía exactamente cuatro campos y exige respuesta 201 con Ticket válido. No usa Location como destino de navegación.
- Botón Nuevo ticket para cuentas autenticadas y título del listado como botón de acceso al detalle. La tabla emite selección y el listado navega, manteniendo el componente de presentación independiente del Router. No es necesario modificar las pruebas anteriores por nuevas dependencias de navegación.
- Página features/tickets/detail, con descripción completa y texto escapado, fecha local y relaciones nullable representadas por ID o ausencia. Comparte estilos de badges mediante un mixin SCSS, y los estilos base de página con creación.
- Guards e interceptor existentes reutilizados sin cambios. Ruta estática new antes del ID; contexto de filtros/página conservado al volver al listado. Confirmación de creación transmitida como estado efímero de navegación, no en el JWT ni en almacenamiento persistente.
- Carga, errores seguros 400/401/403/404/409/red, ticket no disponible, reintento de lectura y cancelación al cambiar ID o salir. Envios bloqueados mientras hay petición pendiente y tras éxito confirmado, incluso si falla la navegación.
- Sin reintentos automáticos del POST. Se explica que timeout, desconexión o cancelar el cliente no garantizan rollback en el servidor. Revisar el listado antes de reenviar una creación incierta; no se propone ni aplica una modificación del backend.

Validaciones realizadas:

- `npm run build`: correcto, sin advertencias. Se conservan los presupuestos SCSS y no se añaden dependencias.
- `npm test -- --watch=false`: **226 superadas, 0 fallidas**, en 19 archivos. Las 162 anteriores permanecen intactas; cuatro nuevos archivos prueban servicio, formulario, detalle y rutas con HTTP simulado. Incluyen UTF-16/espacios, enums, envío duplicado, respuesta 201, Location externo ignorado, confirmación, retorno con filtros, los tres roles, protección anónima, 400/401/403/404/409, red, timeout, respuesta incorrecta, XSS escapado y cancelación.
- `dotnet build HelpDesk.NET.slnx --no-restore --nologo`: correcto, 0 errores y 0 advertencias. El intento restringido falló sin diagnóstico; completado fuera del sandbox con autorización.
- `dotnet test HelpDesk.NET.slnx --no-build --no-restore --filter 'Category!=PostgreSQL' --nologo`: 257 superadas, 0 fallidas/omitidas. Sin ejecutar pruebas PostgreSQL ni migraciones.
- `git diff --check` y revisión de whitespace de los 14 archivos nuevos correctos. Backend, solución, autenticación, proxy, dependencias y los 15 archivos de pruebas Angular anteriores sin cambios. No ha sido necesaria ninguna excepción para modificar pruebas previas.
- Verificado que las fixtures no aparecen en el build de producción. No se utilizan datos simulados en la aplicación.

Limitaciones: la comprobación visual/manual autenticada con datos reales queda para el usuario; no se solicitan credenciales ni se crean tickets reales durante la implementación. La creación manual debe realizarse solo en un destino autorizado para escritura, preferiblemente aislado y ya preparado, siguiendo README. El backend solo ofrece fecha de creación e IDs de usuarios, y no garantiza idempotencia del POST; no se modifica para ampliar ese contrato. Sin commit ni push.
