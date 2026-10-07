# Progreso — HelpDesk.NET

Última actualización: 2026-10-07.

## Funcionalidades completadas

- Contexto educativo, reglas de trabajo y decisiones iniciales documentados.
- Solución `HelpDesk.NET.slnx` creada.
- Proyecto ASP.NET Core Web API `HelpDesk.Api` creado para `net10.0` y añadido a la solución.
- Arranque mínimo en `Program.cs`, sin el ejemplo WeatherForecast.
- Primer endpoint Minimal API: `GET /api/health`, que devuelve HTTP 200 y JSON `{"status":"ok"}` mediante `app.MapGet()`.
- Configuración de aplicación y perfil HTTP local conservados, sin paquetes adicionales.

- Primer modelo de dominio `Ticket` creado en `HelpDesk.Api/Models/Ticket.cs`, con únicamente `Id` (`int`), `Title` (`string`), `Description` (`string`) y `CreatedAt` (`DateTime`).

- Endpoint `GET /api/tickets/{id}`: recibe el parámetro de ruta como `int` y busca en una `List<Ticket>` en memoria con tres tickets de ejemplo (IDs 1, 2 y 3). Usa `FirstOrDefault(ticket => ticket.Id == id)` y devuelve HTTP 200 con el ticket en JSON mediante `Results.Ok(ticket)`, o HTTP 404 sin cuerpo mediante `Results.NotFound()` si no existe.

- Endpoint `POST /api/tickets`: recibe un `Ticket` desde JSON, asigna `Id` con el mayor identificador de la lista más uno y `CreatedAt` con `DateTime.UtcNow`, añade el objeto a la colección y devuelve HTTP 201 con JSON y cabecera `Location` mediante `Results.Created`.

El modelo se utiliza directamente en los endpoints y no tiene persistencia.

## Trabajo actual

Creación de recursos mediante POST completada. ASP.NET Core deserializa el JSON del cuerpo a través del parámetro `(Ticket ticket)`. El endpoint sustituye cualquier `Id` o `CreatedAt` enviado por el cliente por valores calculados en el servidor. `tickets.Add(ticket)` incorpora el objeto a la misma colección que consulta GET; `Results.Created` serializa el ticket y devuelve HTTP 201 junto con su ruta en `Location`.

La colección se crea una vez al arrancar con los IDs 1, 2 y 3. Los nuevos IDs se calculan con `tickets.Max(ticket => ticket.Id) + 1`; la lista inicial no está vacía. Los datos creados se pierden al reiniciar. Se mantienen GET por ID (200 o 404) y health. No se han añadido dependencias ni capas. Pendiente de la siguiente tarea solicitada.

Validación del cambio actual:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- API arrancada en `http://127.0.0.1:5080` con `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`.
- Primer POST real a `/api/tickets` con título y descripción: HTTP 201, ID 4 y `Location: /api/tickets/4`.
- Segundo POST real enviando también `id: 999` y una fecha antigua: HTTP 201, ID 5 y fecha UTC actual asignados por el servidor.
- Aserciones verificaron JSON, las cuatro propiedades, título y descripción conservados, IDs consecutivos, cabecera `Location` y `CreatedAt` en UTC dentro del intervalo de cada petición.
- GET reales a `/api/tickets/4` y `/api/tickets/5`: HTTP 200 con objetos idénticos a los devueltos por los POST, confirmando su incorporación a la colección.
- Peticiones HTTP reales a `/api/tickets/1`, `/api/tickets/2` y `/api/tickets/3`: HTTP 200. Aserciones verificaron el identificador correspondiente, las cuatro propiedades del modelo y contenido de título, descripción y fecha.
- Petición HTTP real a `/api/tickets/99`: HTTP 404 y cuerpo vacío, verificados mediante aserciones.
- Petición HTTP real a `/api/health`: HTTP 200 y JSON `{"status":"ok"}`, verificados mediante aserciones.
- El sandbox bloqueó los sockets del servidor y del cliente; las comprobaciones HTTP se completaron con autorización fuera del sandbox.
- Servidor detenido correctamente tras las comprobaciones.
- No se ejecuta `dotnet test`: aún no existen proyectos de tests.

## Problemas conocidos

- La colección es temporal: los tickets creados se pierden al reiniciar la API.
- `List<Ticket>` y el cálculo `Max + 1` no están sincronizados para peticiones simultáneas. Esta práctica comprueba peticiones secuenciales; la concurrencia queda pendiente.
- No se ha añadido validación de título o descripción en esta etapa.
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
