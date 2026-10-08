# Arquitectura — HelpDesk.NET

Última actualización: 2026-10-08.

## Estado actual

El repositorio contiene la solución `HelpDesk.NET.slnx` y el único proyecto ASP.NET Core Web API `HelpDesk.Api` (`net10.0`). `HelpDeskDbContext` está registrado mediante DI con Npgsql y `ConnectionStrings:DefaultConnection`. El cierre del 8 de octubre de 2026 confirmó mediante consultas de lectura PostgreSQL 16.15, la base `helpdesk_db` y `InitialCreate` aplicada en `__EFMigrationsHistory`. Los dos GET acceden a `Tickets`; la tabla estaba vacía durante las pruebas.

`GET /api/tickets` inyecta el contexto y usa `ToListAsync` para devolver HTTP 200 con el array de tickets, incluido `[]` si la tabla está vacía. `GET /api/tickets/{id}` usa `FirstOrDefaultAsync` y devuelve 200 con el objeto o 404 sin cuerpo. Health mantiene HTTP 200 con `{"status":"ok"}`.

POST, PUT y DELETE siguen trabajando sobre `List<Ticket>` con los tres ejemplos iniciales. Sus DTOs, validación y respuestas no cambian. Durante esta transición las lecturas y escrituras utilizan almacenes independientes: las escrituras en memoria no aparecen en los GET. No hay servicios, repositorios ni capas adicionales.

## Decisiones iniciales

### 001 — Desarrollo incremental orientado al aprendizaje

Estado: aceptada.

Decisión: implementar únicamente tareas solicitadas, en cambios pequeños, explicando antes el concepto .NET y los archivos afectados.

Motivo: comprender las decisiones y el código, y construir un portfolio que refleje aprendizaje profesional.

Consecuencia: el alcance funcional completo se desarrollará por etapas; cada etapa se validará y documentará.

### 002 — Backend previsto con .NET 10, ASP.NET Core Web API y EF Core

Estado: backend y lecturas persistidas implementados; persistencia de escrituras pendiente.

Decisión: utilizar .NET 10 y ASP.NET Core Web API para el backend, e incorporar Entity Framework Core para persistencia en la etapa correspondiente.

Motivo: aprender desarrollo de APIs y acceso a datos dentro del ecosistema .NET.

Consecuencia: entorno comprobado con SDK .NET 10.0.112 y runtime ASP.NET Core 10.0.12. PostgreSQL y Npgsql se han elegido para la primera integración (decisión 012). Se incorporan EF Core/Design `10.0.12` y Npgsql EF Core `10.0.3`, compatibles con `net10.0`. GET utiliza PostgreSQL; las escrituras de los demás endpoints siguen siendo temporales.

### 003 — Estructura inicial sencilla

Estado: aceptada.

Decisión: no adoptar Clean Architecture inicialmente ni añadir capas o patrones sin una necesidad concreta. La estructura inicial consiste en un único proyecto `HelpDesk.Api` en la raíz de la solución.

Motivo: reducir complejidad inicial y centrar el aprendizaje en los fundamentos de .NET y ASP.NET Core.

Consecuencia: la organización podrá evolucionar con motivos documentados cuando el proyecto lo requiera.

### 004 — Alcance del dominio previsto

Estado: alcance aceptado; diseño detallado pendiente.

Decisión: contemplar usuarios, técnicos y administradores, así como incidencias, prioridades, categorías, asignaciones, estados, comentarios e historial.

Motivo: practicar un dominio con relaciones, reglas de negocio y responsabilidades distintas.

Consecuencia: entidades, relaciones, transiciones de estado, permisos y mecanismo de historial se definirán al abordar cada tarea. No se establece todavía un esquema de datos ni un contrato de endpoints.

### 005 — Incorporación posterior de tecnologías adicionales

Estado: prevista; implementación pendiente de solicitud.

Decisión: incorporar Angular y TypeScript para el frontend, autenticación y autorización, tests automatizados y Docker en etapas posteriores. El mecanismo de autenticación, herramientas de tests y configuración de contenedores están pendientes.

Motivo: aprender cada concepto con una base comprensible y evitar introducir tecnologías innecesarias.

Consecuencia: no se generan todavía frontend, infraestructura, autenticación ni proyectos de tests. Cada cambio se verificará desde el inicio con los medios disponibles, aunque la suite automatizada se incorpore después.

### 006 — Secretos fuera del repositorio

Estado: aceptada.

Decisión: nunca incluir secretos ni credenciales en archivos versionados o documentación. El mecanismo de configuración segura se concretará cuando se necesite.

Motivo: mantener prácticas profesionales y evitar exponer datos sensibles.

Consecuencia: los ejemplos utilizarán marcadores y las futuras configuraciones sensibles se suministrarán mediante un mecanismo adecuado al entorno.

### 007 — Arranque mínimo de Web API

Estado: implementada.

Decisión: generar la plantilla `webapi` de .NET 10 con Minimal APIs, sin OpenAPI ni HTTPS local en esta etapa. Eliminar el endpoint WeatherForecast y su archivo de peticiones de ejemplo. Mantener `Program.cs`, el archivo de proyecto, `appsettings.json`, `appsettings.Development.json` y `Properties/launchSettings.json`.

Motivo: aprender primero el arranque de ASP.NET Core y la relación entre solución y proyecto con la estructura mínima solicitada.

Consecuencia: `Program.cs` crea el builder, construye la aplicación y ejecuta el servidor. El primer endpoint se registra directamente con `app.MapGet("/api/health", () => new { status = "ok" })`; no hay controllers ni capas adicionales. HTTP se utiliza para la comprobación local; una petición a una ruta sin endpoint devuelve 404. HTTPS y el estilo de los futuros endpoints podrán revisarse cuando se soliciten.

### 008 — Primer modelo de dominio Ticket

Estado: modelo implementado; consulta inicial en memoria sustituida por EF Core en la decisión 014.

Decisión: ubicar una clase C# sencilla `Ticket` en `HelpDesk.Api/Models/Ticket.cs`, con únicamente `Id` (`int`), `Title` (`string`), `Description` (`string`) y `CreatedAt` (`DateTime`), mediante propiedades automáticas públicas.

Motivo: aprender la diferencia entre clase y objeto y representar una incidencia sin introducir persistencia ni capas adicionales.

Consecuencia inicial (histórica): los textos se inicializan con `string.Empty` para evitar valores nulos; no hay validaciones ni asignación automática de identificador o fecha dentro del modelo. El endpoint POST asigna ambos valores al crear el recurso. En la etapa inicial, la clase se utilizaba en el endpoint `GET /api/tickets/{id}`, que recibe el parámetro de ruta como `int id`, consulta mediante `FirstOrDefault` una `List<Ticket>` creada en `Program.cs` al arrancar, con tres objetos de ejemplo (IDs 1, 2 y 3). Si el resultado es `null`, devuelve `Results.NotFound()` (HTTP 404 sin cuerpo); en caso contrario, devuelve `Results.Ok(ticket)` (HTTP 200 con JSON). Los datos se recrean al reiniciar. Sustituye a la ruta literal `/api/tickets/1`; si el segmento no puede convertirse a `int`, ASP.NET Core responde con HTTP 400 antes de ejecutar la función. Cuando encuentra el ticket, ASP.NET Core lo serializa con System.Text.Json, con nombres de propiedades camelCase y fecha ISO 8601. No incorpora configuración de Entity Framework ni persistencia.

### 009 — Creación de tickets en memoria mediante POST

Estado: implementada para la práctica educativa; entrada evolucionada a DTO en la decisión 010.

Decisión inicial: registrar `POST /api/tickets` directamente en `Program.cs`, recibiendo un `Ticket` desde el cuerpo JSON. La decisión 010 sustituye esta entrada por un DTO para delimitar los datos recibidos. Calcular el ID mediante `tickets.Max(ticket => ticket.Id) + 1`, asignar `DateTime.UtcNow`, añadir el objeto a la lista compartida con GET y responder con `Results.Created($"/api/tickets/{ticket.Id}", ticket)`.

Motivo: aprender deserialización del cuerpo, creación de recursos y la respuesta HTTP 201 sin añadir capas ni dependencias.

Consecuencia actual: el servidor asigna ID y fecha al nuevo ticket; la respuesta incluye el ticket y una cabecera `Location` que permite consultarlo mediante GET. La lista comienza con tres ejemplos, pero DELETE permite dejarla vacía: en ese caso, el cálculo actual con `Max` falla y POST devuelve HTTP 500. Eliminar el ID máximo también permite que `Max + 1` lo reutilice. La revisión del cálculo queda pendiente; POST se mantiene sin cambios en esta etapa. Los datos se pierden al reiniciar. La colección y el cálculo de ID no están sincronizados para peticiones simultáneas; esta implementación temporal se verifica con peticiones secuenciales. El endpoint valida el título obligatorio (máximo 100 caracteres) y la descripción obligatoria antes de asignar ID y fecha o añadir el ticket. Usa `string.IsNullOrWhiteSpace` y `Title.Length`; el primer fallo devuelve HTTP 400 mediante `Results.BadRequest` con un JSON que contiene `error`. Las peticiones inválidas no modifican la colección. La persistencia permanece pendiente.

### 010 — DTO de entrada para crear tickets

Estado: implementada.

Decisión: crear `DTOs/CreateTicketDto.cs` con únicamente `Title` y `Description`, inicializadas con `string.Empty`. POST recibe este DTO, mantiene las validaciones manuales existentes y, si son correctas, construye un `Ticket` mediante un inicializador de objeto. Copia los textos y asigna ID y fecha UTC en el servidor.

Motivo: separar el contrato de creación del modelo de dominio y evitar que propiedades internas se incorporen accidentalmente a la entrada del cliente al evolucionar `Ticket`.

Consecuencia: el DTO no contiene ID ni fecha; los campos JSON adicionales se ignoran con la configuración actual. Los valores ausentes, nulos o inválidos de los textos siguen devolviendo HTTP 400 antes de crear o guardar el ticket. La colección y las respuestas siguen utilizando `Ticket`; HTTP 201 y `Location`, GET y health se mantienen. El mapeo es manual y no requiere servicios, capas ni dependencias adicionales.

### 011 — Actualización en memoria con PUT y validación compartida

Estado: implementada.

Decisión: crear `DTOs/UpdateTicketDto.cs` con `Title` y `Description`, inicializadas con `string.Empty`. Registrar PUT por ID directamente en `Program.cs`. Buscar primero el ticket; si existe, validar y actualizar únicamente sus textos. Devolver HTTP 404 si no existe, HTTP 400 con JSON de error si falla la validación, o HTTP 200 con el objeto actualizado.

Motivo: aprender actualización idempotente y conservar las propiedades administradas por el servidor, evitando duplicar las reglas de creación y actualización.

Consecuencia: ID y fecha de creación permanecen intactos; repetir los mismos datos mantiene el mismo estado. Los DTOs de creación y actualización son independientes. La función local `ValidateTicket` recibe dos textos anulables y devuelve el primer mensaje de error o `null`. Centraliza las reglas ya existentes sin servicios ni capas adicionales; POST conserva sus respuestas y comportamiento. La colección sigue siendo temporal y no está sincronizada para peticiones simultáneas.

### 012 — Preparación de EF Core con PostgreSQL

Estado: contexto, registro y esquema implementados; GET persistidos (decisión 014), escrituras pendientes.

Decisión: añadir EF Core y Design `10.0.12` y el proveedor oficial del proyecto Npgsql `10.0.3` (requiere EF Core >= 10.0.4 y < 11). Design usa `PrivateAssets=all`. Crear `Data/HelpDeskDbContext.cs` en el único proyecto, con constructor que recibe `DbContextOptions<HelpDeskDbContext>` y `DbSet<Ticket> Tickets`. Registrar con `AddDbContext` y `UseNpgsql`; su duración predeterminada es scoped.

Motivo: introducir acceso relacional y DI progresivamente sin reemplazar todavía los endpoints ni añadir servicios o repositorios.

Consecuencia: el mapeo configura ID como clave identity by default, título obligatorio/máximo 100, descripción obligatoria y fecha timestamptz. La clase de dominio no necesita atributos ni propiedades nuevas. `IsRequired` configura nulabilidad del esquema; las reglas contra textos vacíos o espacios siguen siendo responsabilidad de la validación HTTP. Npgsql requiere fechas UTC al persistir timestamptz; los valores actuales ya se crean con UTC.

La cadena se obtiene de `ConnectionStrings:DefaultConnection`, mediante User Secrets en Development o `ConnectionStrings__DefaultConnection` en el entorno. Solo se versiona el identificador no sensible del almacén; no se han guardado credenciales reales. User Secrets no cifra los datos y se limita al desarrollo. El contexto comprueba que hay una cadena al resolverse; el registro no abre conexiones ni crea tablas; los GET lo solicitan ahora para consultar (decisión 014). No se llama a `EnsureCreated`, `Migrate` o `SaveChanges`. La migración inicial está aplicada en `helpdesk_db`, verificada al cierre (decisión 013). Instalación/configuración local y etapas pendientes documentadas en `POSTGRESQL.md`.

### 013 — Primera migración InitialCreate

Estado: generada; aplicada posteriormente por el desarrollador y confirmada mediante consulta del historial al cierre de sesión.

Decisión: alinear la lectura de configuración con `ConnectionStrings:DefaultConnection` en User Secrets y fijar `dotnet-ef` 10.0.12 en el manifiesto local `dotnet-tools.json`. Generar `InitialCreate` mediante el contexto registrado en DI, con entorno Development, sin fábrica adicional.

Motivo: versionar el esquema inicial y permitir reproducir las herramientas, manteniendo secretos fuera del código.

Consecuencia: `Migrations` contiene la creación de `Tickets`, metadatos del modelo objetivo y la instantánea usada para futuras diferencias. `Up` define las cuatro columnas y su clave identity; `Down` describe la reversión. La generación no aplicó SQL ni modificó datos. Después el desarrollador aplicó `InitialCreate`; la revisión de cierre confirmó su registro en `__EFMigrationsHistory` de `helpdesk_db`. No se reaplicó ni revirtió la migración. Los GET ya utilizan esa tabla; las escrituras siguen en memoria.

### 014 — GET asíncronos con EF Core

Estado: implementada; migración de escrituras pendiente.

Decisión: inyectar `HelpDeskDbContext` en las dos Minimal APIs GET. Obtener el listado con `ToListAsync` y buscar por ID con `FirstOrDefaultAsync`, usando `async`/`await` y manteniendo HTTP 200 o 404.

Motivo: aprender consultas asíncronas y DI sobre PostgreSQL de forma incremental.

Consecuencia: GET consulta la tabla persistida y deja de leer los ejemplos en memoria. POST, PUT y DELETE conservan su lista y comportamiento: sus cambios aún no se reflejan en GET. No se generan migraciones ni se modifica el modelo. Las pruebas reales confirmaron 200 con tabla vacía y 404 por ID inexistente; el caso de ID existente queda pendiente por falta de filas.

## Comprobaciones y pendientes al cierre del 8 de octubre de 2026

Build correcto sin errores ni advertencias. GET del listado devolvió 200 con `[]`; GET por ID inexistente, 404 sin cuerpo. No se verificó la rama 200 por ID porque la tabla está vacía. La revisión de archivos versionados/no ignorados no detectó credenciales y staging estaba vacío.

La coexistencia de almacenes es temporal: POST devuelve una ubicación de GET, pero el ticket creado en memoria aún no existe en PostgreSQL. PUT y DELETE tampoco afectan la tabla. Pendiente migrar esas escrituras con operaciones asíncronas y `SaveChangesAsync` por tareas autorizadas, y eliminar la lista al completar esa transición. El cálculo actual `Max + 1` falla con lista vacía y puede reutilizar IDs eliminados; la generación identity está configurada en PostgreSQL pero POST todavía no la utiliza. La lista tampoco está sincronizada para concurrencia. Tests automatizados y futuras funcionalidades siguen pendientes.

## Mantenimiento del registro

Ante un cambio arquitectónico importante, registra la decisión, su estado, motivos y consecuencias. Si reemplaza una decisión previa, conserva el motivo de la evolución y actualiza el estado anterior. Refleja también el avance en `PROGRESS.md`.
