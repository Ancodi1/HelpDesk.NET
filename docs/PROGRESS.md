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

- Endpoint `GET /api/tickets/{id}`: recibe el parámetro de ruta como `int` y busca en una `List<Ticket>` en memoria con tres tickets de ejemplo (IDs 1, 2 y 3). Usa `FirstOrDefault(ticket => ticket.Id == id)` y devuelve HTTP 200 con el ticket en JSON mediante `Results.Ok(ticket)`, o HTTP 404 sin cuerpo mediante `Results.NotFound()` si no existe.

- Endpoint `POST /api/tickets`: recibe un `CreateTicketDto` desde JSON y construye internamente un nuevo `Ticket`, asigna `Id` con el mayor identificador de la lista más uno y `CreatedAt` con `DateTime.UtcNow`, añade el objeto a la colección y devuelve HTTP 201 con JSON y cabecera `Location` mediante `Results.Created`.

- Validación básica de `POST /api/tickets`: título obligatorio con máximo de 100 caracteres y descripción obligatoria. Se rechazan campos ausentes, `null`, vacíos o compuestos solamente por espacios con HTTP 400 y JSON `{"error":"mensaje explicativo"}`. Se devuelve el primer error y no se añade el ticket a la colección.

- Primer DTO `CreateTicketDto` creado en `HelpDesk.Api/DTOs/CreateTicketDto.cs`, con únicamente `Title` y `Description`. Separa la entrada del POST del modelo de dominio.

`Ticket` se utiliza para la colección y las respuestas; POST recibe el DTO. No hay persistencia.

## Trabajo actual

Primer DTO implementado. ASP.NET Core deserializa la petición de POST en `CreateTicketDto`. Se mantienen las validaciones existentes sobre sus propiedades; después se crea un `new Ticket` con título y descripción copiados del DTO, ID calculado y `CreatedAt = DateTime.UtcNow`. Solo el objeto de dominio se añade a la colección y se devuelve con HTTP 201 y `Location`. El DTO no contiene ID ni fecha: las propiedades JSON adicionales se ignoran con la configuración actual. GET y health mantienen su código. No se añaden dependencias, controllers, servicios ni capas. Pendiente de la siguiente tarea solicitada.

Validación del cambio actual:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- API arrancada en `http://127.0.0.1:5080` con `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`.
- 11 POST inválidos: título y descripción ausentes, `null`, vacíos, con espacios o tabulaciones/saltos de línea; además, título de 101 caracteres. Todos devolvieron HTTP 400 con JSON explicativo, verificado mediante aserciones.
- Tras cada POST inválido, GET al ID 4 devolvió 404. El primer POST válido recibió ID 4, confirmando que los rechazos no añadieron tickets.
- Dos POST válidos, incluido un título de exactamente 100 caracteres: HTTP 201, IDs consecutivos 4 y 5, contenido conservado y cabecera `Location` correctos. El primero envió únicamente título y descripción. El segundo añadió `id: 999` y un `createdAt` que no era una fecha; esos campos se ignoraron al no pertenecer al DTO. Se verificaron las cuatro propiedades de la respuesta y la fecha UTC dentro del intervalo de cada petición.
- GET de los tickets creados devolvió HTTP 200 y el mismo JSON que POST. GET de los ejemplos 1, 2 y 3, GET inexistente (404) y health (200 con JSON esperado) siguen funcionando.
- Las comprobaciones HTTP se ejecutaron con autorización fuera del sandbox, porque en esta sesión ya se había comprobado que bloquea los sockets locales.
- Servidor detenido tras las comprobaciones.
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
