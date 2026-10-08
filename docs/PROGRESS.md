# Progreso — HelpDesk.NET

Última actualización: 2026-10-08.

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

- Endpoint `POST /api/tickets`: recibe un `CreateTicketDto` desde JSON y construye internamente un nuevo `Ticket`, asigna `Id` con el mayor identificador de la lista más uno y `CreatedAt` con `DateTime.UtcNow`, añade el objeto a la colección y devuelve HTTP 201 con JSON y cabecera `Location` mediante `Results.Created`.

- Validación compartida de POST y PUT mediante la función local `ValidateTicket` en `Program.cs`: título obligatorio con máximo de 100 caracteres y descripción obligatoria. Se rechazan campos ausentes, `null`, vacíos o compuestos solamente por espacios con HTTP 400 y JSON `{"error":"mensaje explicativo"}`. Se devuelve el primer error sin añadir ni modificar tickets.

- Primer DTO `CreateTicketDto` creado en `HelpDesk.Api/DTOs/CreateTicketDto.cs`, con únicamente `Title` y `Description`. Separa la entrada del POST del modelo de dominio.

- Endpoint `PUT /api/tickets/{id}` con `UpdateTicketDto` (`Title` y `Description`): busca el ticket, devuelve 404 si no existe, valida los textos y actualiza únicamente esos dos campos. Conserva ID y fecha de creación y devuelve HTTP 200 con el ticket actualizado.

- Endpoint `DELETE /api/tickets/{id}`: busca el ticket en la lista compartida, devuelve HTTP 404 sin cuerpo si no existe, o lo elimina y devuelve HTTP 204 sin cuerpo.

- Preparación de PostgreSQL y EF Core: paquetes EF Core/Design `10.0.12` y Npgsql EF Core `10.0.3`; `Data/HelpDeskDbContext.cs`, `DbSet<Ticket>` y mapeo del modelo; contexto registrado mediante DI y cadena de conexión externa `ConnectionStrings:DefaultConnection` con User Secrets o variable de entorno. Guía Linux Mint 22.3 en `docs/POSTGRESQL.md`. Migración `InitialCreate` aplicada por el desarrollador; los dos GET ya consultan PostgreSQL.

`Ticket` se utiliza para la colección y las respuestas; POST y PUT reciben sus respectivos DTOs. GET lee datos persistidos en PostgreSQL; POST, PUT y DELETE todavía operan en memoria.

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

## Problemas conocidos

- Estado transitorio: GET lee PostgreSQL; POST, PUT y DELETE siguen en memoria. Crear, actualizar o eliminar mediante esos endpoints no modifica los resultados de GET.
- Tabla vacía durante las pruebas: pendiente comprobar GET por ID existente cuando haya datos persistidos.
- Tras eliminar todos los tickets, POST válido devuelve HTTP 500: `Max` falla sobre la lista vacía. Además, `Max + 1` puede reutilizar el ID del ticket de mayor identificador si se elimina. El cálculo de IDs queda pendiente de revisión en una tarea autorizada; POST no se ha modificado.
- La colección es temporal: los tickets creados se pierden al reiniciar la API.
- `List<Ticket>` y el cálculo `Max + 1` no están sincronizados para peticiones simultáneas. Esta práctica comprueba peticiones secuenciales; la concurrencia queda pendiente.
- El sandbox bloqueó inicialmente la apertura del socket; la comprobación de arranque se completó con autorización fuera del sandbox.
- SDK comprobado: .NET 10.0.112; runtime ASP.NET Core 10.0.12.
- No hay proyectos de tests automatizados todavía.
- Las rutas sin un endpoint registrado devolverán 404.

## Próximos pasos

Estos pasos son orientativos y requieren una tarea solicitada:

1. Migrar POST a EF Core cuando se solicite: generación de ID en PostgreSQL y guardado con `SaveChangesAsync`, conservando validación y respuesta 201.
2. Verificar GET por ID con un ticket persistido; después migrar PUT y DELETE por etapas autorizadas. Retirar la lista únicamente cuando ningún endpoint dependa de ella.
3. Incorporar usuarios, técnicos, administradores, prioridades, categorías, asignaciones, estados, comentarios e historial por tareas concretas.
4. Añadir autenticación y autorización, tests automatizados, frontend con Angular y TypeScript y Docker en etapas posteriores.

Cada cambio importante debe actualizar este documento y, si afecta al diseño, `ARCHITECTURE.md`.
