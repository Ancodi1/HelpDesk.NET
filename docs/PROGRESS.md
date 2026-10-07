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

El modelo se utiliza en este endpoint de ejemplo y no tiene persistencia.

## Trabajo actual

Colecciones en memoria y respuestas HTTP completadas. La lista se crea una vez al arrancar la aplicación; cada petición consulta los objetos existentes. `FirstOrDefault` devuelve el primer ticket cuyo `Id` coincide, o `null` si no hay coincidencias. La comprobación `ticket is null` permite elegir entre 404 y 200. Los datos son ejemplos sin persistencia y se recrean al reiniciar. El parámetro sigue siendo `int`; si la conversión falla, ASP.NET Core devuelve HTTP 400 antes de ejecutar la función. Pendiente de la siguiente tarea solicitada.

Validación del cambio actual:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- API arrancada en `http://127.0.0.1:5080` con `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`.
- Peticiones HTTP reales a `/api/tickets/1`, `/api/tickets/2` y `/api/tickets/3`: HTTP 200. Aserciones verificaron el identificador correspondiente, las cuatro propiedades del modelo y contenido de título, descripción y fecha.
- Petición HTTP real a `/api/tickets/99`: HTTP 404 y cuerpo vacío, verificados mediante aserciones.
- Petición HTTP real a `/api/health`: HTTP 200 y JSON `{"status":"ok"}`, verificados mediante aserciones.
- El sandbox bloqueó los sockets del servidor y del cliente; las comprobaciones HTTP se completaron con autorización fuera del sandbox.
- Servidor detenido correctamente tras las comprobaciones.
- No se ejecuta `dotnet test`: aún no existen proyectos de tests.

## Problemas conocidos

- No se han identificado problemas de implementación.
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
