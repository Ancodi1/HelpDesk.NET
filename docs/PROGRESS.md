# Progreso — HelpDesk.NET

Última actualización: 2026-10-06.

## Funcionalidades completadas

- Contexto educativo, reglas de trabajo y decisiones iniciales documentados.
- Solución `HelpDesk.NET.slnx` creada.
- Proyecto ASP.NET Core Web API `HelpDesk.Api` creado para `net10.0` y añadido a la solución.
- Arranque mínimo en `Program.cs`, sin el ejemplo WeatherForecast.
- Primer endpoint Minimal API: `GET /api/health`, que devuelve HTTP 200 y JSON `{"status":"ok"}` mediante `app.MapGet()`.
- Configuración de aplicación y perfil HTTP local conservados, sin paquetes adicionales.

- Primer modelo de dominio `Ticket` creado en `HelpDesk.Api/Models/Ticket.cs`, con únicamente `Id` (`int`), `Title` (`string`), `Description` (`string`) y `CreatedAt` (`DateTime`).

- Endpoint `GET /api/tickets/{id}`: recibe el parámetro de ruta como `int`, crea en memoria un `Ticket` con `Id = id` y lo devuelve con HTTP 200 mediante serialización JSON automática. Sustituye al endpoint literal `GET /api/tickets/1`.

El modelo se utiliza en este endpoint de ejemplo y no tiene persistencia.

## Trabajo actual

Parámetros de ruta completados: `{id}` captura un segmento de la URL y ASP.NET Core lo enlaza con el argumento `(int id)` del endpoint. Convierte el texto a entero antes de ejecutar la función; si la conversión falla, devuelve HTTP 400. El endpoint crea un nuevo `Ticket` por petición con el identificador recibido, sin consultar ni guardar datos. Pendiente de la siguiente tarea solicitada.

Validación del cambio actual:

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- API arrancada en `http://127.0.0.1:5080` con `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`.
- Peticiones HTTP reales a `/api/tickets/1` y `/api/tickets/25`: HTTP 200 y contenido JSON. Aserciones verificaron las cuatro propiedades del modelo y que `id` es un número entero con valor 1 y 25, respectivamente.
- Ambos tickets mantienen el título y la descripción de ejemplo y `createdAt` igual a `2026-10-06T10:00:00Z`.
- Petición HTTP real a `/api/tickets/abc`: HTTP 400, confirmado mediante una aserción.
- El sandbox bloqueó los sockets del servidor y del cliente; las comprobaciones HTTP se completaron con autorización fuera del sandbox.
- Servidor detenido correctamente tras las comprobaciones.
- No se ejecuta `dotnet test`: aún no existen proyectos de tests.

Validación HTTP realizada en la tarea anterior (sin cambios en este endpoint):

- `dotnet build HelpDesk.NET.slnx`: correcto, 0 errores y 0 advertencias.
- `dotnet run --project HelpDesk.Api/HelpDesk.Api.csproj --no-build --no-launch-profile --urls http://127.0.0.1:5080`: arranque correcto, confirmado por Kestrel.
- Petición HTTP real a `/api/health`: HTTP 200, `Content-Type: application/json; charset=utf-8` y cuerpo `{"status":"ok"}`, verificados mediante aserciones.
- La respuesta indica que la API atiende peticiones; no comprueba dependencias externas.
- Servidor detenido correctamente tras la comprobación.
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
