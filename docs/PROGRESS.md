# Progreso — HelpDesk.NET

Última actualización: 2026-10-08.

## Funcionalidades completadas

- Contexto educativo, reglas de trabajo y decisiones iniciales documentados.
- Solución `HelpDesk.NET.slnx` creada.
- Proyecto ASP.NET Core Web API `HelpDesk.Api` creado para `net10.0` y añadido a la solución.
- Arranque mínimo en `Program.cs`, sin el ejemplo WeatherForecast.
- Primer endpoint Minimal API: `GET /api/health`, que devuelve HTTP 200 y JSON `{"status":"ok"}` mediante `app.MapGet()`.
- Configuración de aplicación y perfil HTTP local conservados, sin paquetes adicionales.

- Primer modelo de dominio `Ticket` creado en `HelpDesk.Api/Models/Ticket.cs`, con únicamente `Id` (`int`), `Title` (`string`), `Description` (`string`) y `CreatedAt` (`DateTime`).

- Endpoint `GET /api/tickets`: devuelve HTTP 200 con todos los tickets de la lista en memoria mediante `Results.Ok(tickets)`. La respuesta es un array JSON; una lista vacía se devuelve como `[]`.

- Endpoint `GET /api/tickets/{id}`: recibe el parámetro de ruta como `int` y busca en una `List<Ticket>` en memoria con tres tickets de ejemplo (IDs 1, 2 y 3). Usa `FirstOrDefault(ticket => ticket.Id == id)` y devuelve HTTP 200 con el ticket en JSON mediante `Results.Ok(ticket)`, o HTTP 404 sin cuerpo mediante `Results.NotFound()` si no existe.

- Endpoint `POST /api/tickets`: recibe un `CreateTicketDto` desde JSON y construye internamente un nuevo `Ticket`, asigna `Id` con el mayor identificador de la lista más uno y `CreatedAt` con `DateTime.UtcNow`, añade el objeto a la colección y devuelve HTTP 201 con JSON y cabecera `Location` mediante `Results.Created`.

- Validación compartida de POST y PUT mediante la función local `ValidateTicket` en `Program.cs`: título obligatorio con máximo de 100 caracteres y descripción obligatoria. Se rechazan campos ausentes, `null`, vacíos o compuestos solamente por espacios con HTTP 400 y JSON `{"error":"mensaje explicativo"}`. Se devuelve el primer error sin añadir ni modificar tickets.

- Primer DTO `CreateTicketDto` creado en `HelpDesk.Api/DTOs/CreateTicketDto.cs`, con únicamente `Title` y `Description`. Separa la entrada del POST del modelo de dominio.

- Endpoint `PUT /api/tickets/{id}` con `UpdateTicketDto` (`Title` y `Description`): busca el ticket, devuelve 404 si no existe, valida los textos y actualiza únicamente esos dos campos. Conserva ID y fecha de creación y devuelve HTTP 200 con el ticket actualizado.

- Endpoint `DELETE /api/tickets/{id}`: busca el ticket en la lista compartida, devuelve HTTP 404 sin cuerpo si no existe, o lo elimina y devuelve HTTP 204 sin cuerpo.

`Ticket` se utiliza para la colección y las respuestas; POST y PUT reciben sus respectivos DTOs. No hay persistencia.

## Trabajo actual

Eliminación de tickets implementada directamente en `Program.cs` mediante `app.MapDelete`. Se busca el objeto con `FirstOrDefault`; si es `null`, se devuelve `Results.NotFound()`. Si existe, `tickets.Remove(ticket)` lo elimina y `Results.NoContent()` devuelve HTTP 204 sin cuerpo. Repetir la eliminación devuelve 404 y conserva el estado de ausencia del recurso (idempotencia). GET, POST, PUT y health mantienen su código. No se añaden DTOs, dependencias ni capas. Pendiente de la siguiente tarea solicitada.

Validación del cambio actual:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- API arrancada en `http://127.0.0.1:5080` con `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`.
- DELETE del ticket 2: HTTP 204 con cuerpo vacío. GET posterior: HTTP 404 con cuerpo vacío. El listado conservó exactamente los tickets 1 y 3, sin alterar sus propiedades.
- DELETE repetido del ticket 2 y DELETE del ID 99: HTTP 404 sin cuerpo; listado sin cambios.
- GET por ID de los tickets restantes: HTTP 200 con sus objetos originales.
- POST válido tras borrar el ticket 2: HTTP 201, ID 4, textos conservados, fecha UTC actual y `Location` correcto. PUT del ticket creado: HTTP 200, textos actualizados e ID y fecha conservados. GET por ID y listado reflejaron esos cambios.
- POST y PUT con título de espacios: HTTP 400 con JSON de error. PUT al ID 99: HTTP 404. Health: HTTP 200 con JSON esperado.
- Eliminados los tickets restantes: cada DELETE devolvió HTTP 204 sin cuerpo. El listado final devolvió HTTP 200 y cuerpo `[]`. Comprobaciones funcionales realizadas con aserciones.
- POST válido con la lista ya vacía: HTTP 500, confirmado por petición real. El cálculo existente de ID con `Max` no admite una colección vacía; se registra como problema conocido sin modificar POST, conforme al alcance solicitado.
- Comprobaciones HTTP ejecutadas con autorización fuera del sandbox, que bloquea los sockets locales. Servidor detenido tras las pruebas.
- No se ejecuta `dotnet test`: aún no existen proyectos de tests automatizados.

## Problemas conocidos

- Tras eliminar todos los tickets, POST válido devuelve HTTP 500: `Max` falla sobre la lista vacía. Además, `Max + 1` puede reutilizar el ID del ticket de mayor identificador si se elimina. El cálculo de IDs queda pendiente de revisión en una tarea autorizada; POST no se ha modificado.
- La colección es temporal: los tickets creados se pierden al reiniciar la API.
- `List<Ticket>` y el cálculo `Max + 1` no están sincronizados para peticiones simultáneas. Esta práctica comprueba peticiones secuenciales; la concurrencia queda pendiente.
- El sandbox bloqueó inicialmente la apertura del socket; la comprobación de arranque se completó con autorización fuera del sandbox.
- SDK comprobado: .NET 10.0.112; runtime ASP.NET Core 10.0.12.
- No hay proyectos de tests automatizados todavía.
- Las rutas sin un endpoint registrado devolverán 404.

## Próximos pasos

Estos pasos son orientativos y requieren una tarea solicitada:

1. Acordar el siguiente paso de la gestión de tickets y definir sus reglas progresivamente.
2. Incorporar persistencia con Entity Framework Core cuando corresponda.
3. Incorporar usuarios, técnicos, administradores, prioridades, categorías, asignaciones, estados, comentarios e historial por tareas concretas.
4. Añadir autenticación y autorización, tests automatizados, frontend con Angular y TypeScript y Docker en etapas posteriores.

Cada cambio importante debe actualizar este documento y, si afecta al diseño, `ARCHITECTURE.md`.
