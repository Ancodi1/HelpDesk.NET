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

`Ticket` se utiliza para la colección y las respuestas; POST y PUT reciben sus respectivos DTOs. No hay persistencia.

## Trabajo actual

Actualización de tickets implementada mediante PUT. `UpdateTicketDto` delimita los campos editables. El endpoint busca con `FirstOrDefault`, devuelve `Results.NotFound()` si no existe y valida antes de modificar. Asigna únicamente `Title` y `Description` al objeto existente y devuelve `Results.Ok(ticket)`. Repetir la misma petición deja el ticket en el mismo estado (idempotencia); no crea recursos ni cambia `CreatedAt`.

Las reglas existentes se han extraído a la función local `ValidateTicket(string? title, string? description)` en `Program.cs`. Devuelve el primer mensaje de error o `null`; POST y PUT la utilizan para devolver HTTP 400 con el mismo JSON. No se añaden capas, servicios ni dependencias. GET y health conservan su código y POST mantiene su comportamiento. Pendiente de la siguiente tarea solicitada.

Validación del cambio actual:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- API arrancada en `http://127.0.0.1:5080` con `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`.
- 11 PUT inválidos y 11 POST inválidos: campos ausentes, `null`, vacíos, solo espacios o tabulaciones/saltos de línea, y título de 101 caracteres. Aserciones verificaron HTTP 400, JSON de errores idéntico entre ambos endpoints y colección sin cambios tras cada petición.
- PUT al ID 99 con datos válidos y con textos inválidos: HTTP 404 sin cuerpo, sin crear recursos. La búsqueda precede a la validación de campos.
- PUT válido al ID 1: HTTP 200 y textos actualizados. ID y `CreatedAt` iguales a los originales, incluso enviando esas propiedades adicionales en el JSON. Repetir exactamente la petición devolvió el mismo objeto y mantuvo el estado, sin duplicar tickets.
- PUT con título de exactamente 100 caracteres: HTTP 200. GET por ID y listado reflejan las actualizaciones; el resto de tickets permanece intacto.
- POST válido: HTTP 201, ID 4, textos conservados, fecha UTC dentro del intervalo de la petición y `Location: /api/tickets/4`. GET por ID y listado muestran el ticket creado.
- GET inexistente: HTTP 404. Health: HTTP 200 y JSON esperado. Comprobados mediante aserciones.
- Comprobaciones HTTP ejecutadas con autorización fuera del sandbox, que bloquea los sockets locales. Servidor detenido tras las pruebas.
- No se ejecuta `dotnet test`: aún no existen proyectos de tests automatizados. Se ejecutaron comprobaciones funcionales HTTP con aserciones.

## Problemas conocidos

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
