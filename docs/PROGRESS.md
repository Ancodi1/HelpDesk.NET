# Progreso — HelpDesk.NET

Última actualización: 2026-10-13.

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

- Endpoint `DELETE /api/tickets/{id}`: busca el ticket en la lista compartida, devuelve HTTP 404 sin cuerpo si no existe, o lo elimina y devuelve HTTP 204 sin cuerpo.

- Preparación de PostgreSQL y EF Core: paquetes EF Core/Design `10.0.12` y Npgsql EF Core `10.0.3`; `Data/HelpDeskDbContext.cs`, `DbSet<Ticket>` y mapeo del modelo; contexto registrado mediante DI y cadena de conexión externa `ConnectionStrings:DefaultConnection` con User Secrets o variable de entorno. Guía Linux Mint 22.3 en `docs/POSTGRESQL.md`. Migración `InitialCreate` aplicada por el desarrollador; los dos GET ya consultan PostgreSQL.

`Ticket` se utiliza para la colección y las respuestas; POST y PUT reciben sus respectivos DTOs. GET, POST y PUT utilizan PostgreSQL; DELETE todavía opera en memoria.

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

- Estado transitorio: GET, POST y PUT utilizan PostgreSQL, pero DELETE sigue usando los tres ejemplos en memoria. Sus eliminaciones no afectan a GET ni a los tickets persistidos. Coincidir en ID entre ambos almacenes no implica que sea el mismo ticket.
- `List<Ticket>` sigue siendo temporal y no está sincronizada para peticiones simultáneas de DELETE. Sus cambios se pierden al reiniciar.
- POST ya no depende de `Max + 1`: los anteriores problemas con lista vacía, reutilización del máximo eliminado y cálculo concurrente de IDs quedan resueltos al delegar la generación en PostgreSQL.
- SDK comprobado: .NET 10.0.112; runtime ASP.NET Core 10.0.12.
- No hay proyectos de tests automatizados todavía.
- Las rutas sin un endpoint registrado devolverán 404.

## Próximos pasos

Estos pasos son orientativos y requieren una tarea solicitada:

1. Migrar DELETE a EF Core en una tarea autorizada. Retirar la lista únicamente cuando ningún endpoint dependa de ella.
2. Incorporar usuarios, técnicos, administradores, prioridades, categorías, asignaciones, estados, comentarios e historial por tareas concretas.
3. Añadir autenticación y autorización, tests automatizados, frontend con Angular y TypeScript y Docker en etapas posteriores.

Cada cambio importante debe actualizar este documento y, si afecta al diseño, `ARCHITECTURE.md`.
