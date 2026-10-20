# Progreso — HelpDesk.NET

Última actualización: 2026-10-20.

## Funcionalidades completadas

- Contexto educativo, reglas de trabajo y decisiones iniciales documentados.
- Solución `HelpDesk.NET.slnx` creada.
- Proyecto ASP.NET Core Web API `HelpDesk.Api` creado para `net10.0` y añadido a la solución.
- Arranque mínimo en `Program.cs`, sin el ejemplo WeatherForecast.
- Primer endpoint Minimal API: `GET /api/health`, que devuelve HTTP 200 y JSON `{"status":"ok"}` mediante `app.MapGet()`.
- Configuración de aplicación y perfil HTTP local conservados. Dependencias de EF Core y PostgreSQL añadidas en la etapa de preparación.

- Primer modelo de dominio `Ticket` creado en `HelpDesk.Api/Models/Ticket.cs`, con únicamente `Id` (`int`), `Title` (`string`), `Description` (`string`) y `CreatedAt` (`DateTime`).

- Endpoint `GET /api/tickets`: recibe `HelpDeskDbContext` mediante DI y consulta PostgreSQL con `await dbContext.Tickets.ToListAsync()`. Devuelve HTTP 200 con un array JSON; una tabla vacía se devuelve como `[]`.

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
- `HelpDesk.Tests`: 16 pruebas unitarias de transiciones y una prueba de integración HTTP de health con WebApplicationFactory. Infraestructura y tres pruebas PostgreSQL preparadas, pendientes de configuración y ejecución; guía en POSTGRESQL_TESTS.md.
- Conflicto MSB3277 resuelto: referencia explícita a Microsoft.EntityFrameworkCore.Relational 10.0.12 en la API, propagada a Tests. Restore, build y 16 tests sin advertencias.
- Las rutas sin un endpoint registrado devolverán 404.

## Próximos pasos

Estos pasos son orientativos y requieren una tarea solicitada:

1. Ampliar las funcionalidades del dominio únicamente mediante tareas solicitadas; las transiciones de estado ya están implementadas.
2. Incorporar usuarios, técnicos, administradores, prioridades, categorías, asignaciones, reglas de transición de estados, comentarios e historial por tareas concretas.
3. Añadir autenticación y autorización, ampliar los tests automatizados, incorporar frontend con Angular y TypeScript y Docker en etapas posteriores.

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
