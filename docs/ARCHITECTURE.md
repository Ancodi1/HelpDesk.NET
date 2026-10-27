# Arquitectura — HelpDesk.NET

Última actualización: 2026-10-27.

## Estado actual

Fases 10.1 y 10.2: cliente Angular independiente en `HelpDesk.Web`. A la conexión health y layout inicial se añaden login/registro, sesión solo en memoria, interceptor y guards, sin cambios de API ni base de datos. Decisiones 039 y 040 al final.


Revisión del 23 de octubre de 2026: los tres fallos PostgreSQL comunicados en 9.4 se abordan en las pruebas, sin cambios del protocolo de bloqueo, endpoints, esquema ni dependencias. Las comparaciones POST/persistencia usan microsegundos y la barrera de reasignación se sitúa después del UPDATE, antes del commit explícito. Se cubren ambos órdenes con pg_blocking_pids y rechazo del antiguo técnico. Las correcciones quedaron validadas por las 281 pruebas PostgreSQL superadas que confirma el usuario al cierre; junto con las 257 sin PostgreSQL suman 538 superadas y 0 fallidas. Resultados en PROGRESS.md. No constituye una nueva decisión de arquitectura.

La etapa 9.3 protege todas las rutas excepto health/register/login mediante JWT y comprobación de cuenta/rol actual en PostgreSQL. Tickets se filtran por propietario o asignación; la gestión completa y administración de usuarios requieren Admin (decisión 037). La etapa 9.4 incorpora una consola manual del primer Admin, sin cambiar rutas ni permisos; creación real y acceso administrativo confirmados por el usuario al cerrar 9.4 el 27 de octubre de 2026 (decisión 038). Las decisiones anteriores registran la evolución histórica.

El repositorio contiene la solución `HelpDesk.NET.slnx` y los proyectos `HelpDesk.Api` (ASP.NET Core Web API), `HelpDesk.AdminBootstrap` (consola independiente) y `HelpDesk.Tests` (xUnit e integración HTTP), todos para `net10.0`. `HelpDeskDbContext` está registrado mediante DI con Npgsql y `ConnectionStrings:DefaultConnection`. El cierre del 8 de octubre de 2026 confirmó mediante consultas de lectura PostgreSQL 16.15, la base `helpdesk_db` y `InitialCreate` aplicada en `__EFMigrationsHistory`. Todo el CRUD accede a `Tickets`; la comprobación del 13 de octubre confirmó creación y lectura de un ticket incluso tras reiniciar la API.

`GET /api/tickets` compone un IQueryable con AsNoTracking, filtros y búsqueda ILIKE parametrizada; cuenta antes de ordenar por CreatedAt/Id descendentes y aplicar Skip/Take. Devuelve HTTP 200 con items y metadatos de paginación (decisión 030). `GET /api/tickets/{id}` usa `FirstOrDefaultAsync` y devuelve 200 con el objeto o 404 sin cuerpo. Health mantiene HTTP 200 con `{"status":"ok"}`.

POST inyecta el contexto, registra el nuevo ticket con `Add` y lo persiste con `SaveChangesAsync` antes de responder 201. PUT consulta una entidad seguida por EF Core con `FirstOrDefaultAsync`, valida y guarda sus textos con `SaveChangesAsync` antes de responder 200. DELETE consulta por ID con `FirstOrDefaultAsync`, elimina mediante `Remove` y persiste con `SaveChangesAsync`; devuelve 404 o 204. La lista temporal ya no existe. Los handlers de datos usan directamente EF Core; la emisión JWT se encapsula en un servicio pequeño, sin repositorios ni capas adicionales.

La serialización JSON de las Minimal APIs se configura centralmente con `ConfigureHttpJsonOptions` y `JsonStringEnumConverter`: los estados se devuelven como texto (decisión 019). Las referencias a JSON numérico en decisiones anteriores son históricas.

PATCH aplica las transiciones definidas en `TicketStatusTransitions` y devuelve 409 para cambios prohibidos; repetir el estado actual devuelve 200 sin guardar (decisión 021).

Ticket incorpora Priority (Medium por defecto), independiente de Status. POST permite indicarla y PATCH /api/tickets/{id}/priority la modifica sin afectar otros campos. AddTicketPriority está aplicada según el desarrollador; véase su evolución en decisión 028.

Category clasifica el ticket mediante TicketCategory, con Other por defecto. Se admite en POST y se modifica mediante PATCH /api/tickets/{id}/category; AddTicketCategory está aplicada según el desarrollador (decisión 029).

Ticket referencia opcionalmente a User como creador y asignado mediante dos relaciones independientes (decisión 033). La etapa 8.2 añade GET (listado/ID), POST y PUT con DTOs y validación compartida (decisión 032). El desarrollador comunica AddUsers aplicada a desarrollo y las 318 pruebas hasta la etapa 8.2 superadas; esta sesión no conecta a desarrollo.

## Decisiones iniciales

### 001 — Desarrollo incremental orientado al aprendizaje

Estado: aceptada.

Decisión: implementar únicamente tareas solicitadas, en cambios pequeños, explicando antes el concepto .NET y los archivos afectados.

Motivo: comprender las decisiones y el código, y construir un portfolio que refleje aprendizaje profesional.

Consecuencia: el alcance funcional completo se desarrollará por etapas; cada etapa se validará y documentará.

### 002 — Backend previsto con .NET 10, ASP.NET Core Web API y EF Core

Estado: CRUD completo persistido; migración de estado aplicada por el desarrollador y PATCH de estado implementado.

Decisión: utilizar .NET 10 y ASP.NET Core Web API para el backend, e incorporar Entity Framework Core para persistencia en la etapa correspondiente.

Motivo: aprender desarrollo de APIs y acceso a datos dentro del ecosistema .NET.

Consecuencia: entorno comprobado con SDK .NET 10.0.112 y runtime ASP.NET Core 10.0.12. PostgreSQL y Npgsql se han elegido para la primera integración (decisión 012). Se incorporan EF Core/Design `10.0.12` y Npgsql EF Core `10.0.3`, compatibles con `net10.0`. Todo el CRUD utiliza PostgreSQL.

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

Estado: implementación inicial histórica; entrada evolucionada a DTO en la decisión 010 y persistencia sustituida en la decisión 015.

Decisión inicial: registrar `POST /api/tickets` directamente en `Program.cs`, recibiendo un `Ticket` desde el cuerpo JSON. La decisión 010 sustituye esta entrada por un DTO para delimitar los datos recibidos. Calcular el ID mediante `tickets.Max(ticket => ticket.Id) + 1`, asignar `DateTime.UtcNow`, añadir el objeto a la lista compartida con GET y responder con `Results.Created($"/api/tickets/{ticket.Id}", ticket)`.

Motivo: aprender deserialización del cuerpo, creación de recursos y la respuesta HTTP 201 sin añadir capas ni dependencias.

Consecuencia histórica (antes de la decisión 015): el servidor asigna ID y fecha al nuevo ticket; la respuesta incluye el ticket y una cabecera `Location` que permite consultarlo mediante GET. La lista comienza con tres ejemplos, pero DELETE permite dejarla vacía: en ese caso, el cálculo actual con `Max` falla y POST devuelve HTTP 500. Eliminar el ID máximo también permite que `Max + 1` lo reutilice. La revisión del cálculo queda pendiente; POST se mantiene sin cambios en esta etapa. Los datos se pierden al reiniciar. La colección y el cálculo de ID no están sincronizados para peticiones simultáneas; esta implementación temporal se verifica con peticiones secuenciales. El endpoint valida el título obligatorio (máximo 100 caracteres) y la descripción obligatoria antes de asignar ID y fecha o añadir el ticket. Usa `string.IsNullOrWhiteSpace` y `Title.Length`; el primer fallo devuelve HTTP 400 mediante `Results.BadRequest` con un JSON que contiene `error`. Las peticiones inválidas no modifican la colección. La persistencia permanece pendiente.

### 010 — DTO de entrada para crear tickets

Estado: implementada.

Decisión: crear `DTOs/CreateTicketDto.cs` con únicamente `Title` y `Description`, inicializadas con `string.Empty`. POST recibe este DTO, mantiene las validaciones manuales existentes y, si son correctas, construye un `Ticket` mediante un inicializador de objeto. Copia los textos y asigna fecha UTC en el servidor; desde la decisión 015 el ID lo genera PostgreSQL.

Motivo: separar el contrato de creación del modelo de dominio y evitar que propiedades internas se incorporen accidentalmente a la entrada del cliente al evolucionar `Ticket`.

Consecuencia: el DTO no contiene ID ni fecha; los campos JSON adicionales se ignoran con la configuración actual. Los valores ausentes, nulos o inválidos de los textos siguen devolviendo HTTP 400 antes de crear o guardar el ticket. La colección y las respuestas siguen utilizando `Ticket`; HTTP 201 y `Location`, GET y health se mantienen. El mapeo es manual y no requiere servicios, capas ni dependencias adicionales.

### 011 — Actualización en memoria con PUT y validación compartida

Estado: implementación inicial histórica; persistencia sustituida en la decisión 016.

Decisión: crear `DTOs/UpdateTicketDto.cs` con `Title` y `Description`, inicializadas con `string.Empty`. Registrar PUT por ID directamente en `Program.cs`. Buscar primero el ticket; si existe, validar y actualizar únicamente sus textos. Devolver HTTP 404 si no existe, HTTP 400 con JSON de error si falla la validación, o HTTP 200 con el objeto actualizado.

Motivo: aprender actualización idempotente y conservar las propiedades administradas por el servidor, evitando duplicar las reglas de creación y actualización.

Consecuencia: ID y fecha de creación permanecen intactos; repetir los mismos datos mantiene el mismo estado. Los DTOs de creación y actualización son independientes. La función local `ValidateTicket` recibe dos textos anulables y devuelve el primer mensaje de error o `null`. Centraliza las reglas ya existentes sin servicios ni capas adicionales; POST conserva sus respuestas y comportamiento. En esta etapa inicial, la colección era temporal y no estaba sincronizada para peticiones simultáneas.

### 012 — Preparación de EF Core con PostgreSQL

Estado: contexto, registro y esquema implementados; GET persistidos (decisión 014), POST persistido (decisión 015) y PUT persistido (decisión 016); DELETE pendiente.

Decisión: añadir EF Core y Design `10.0.12` y el proveedor oficial del proyecto Npgsql `10.0.3` (requiere EF Core >= 10.0.4 y < 11). Design usa `PrivateAssets=all`. Crear `Data/HelpDeskDbContext.cs` en el único proyecto, con constructor que recibe `DbContextOptions<HelpDeskDbContext>` y `DbSet<Ticket> Tickets`. Registrar con `AddDbContext` y `UseNpgsql`; su duración predeterminada es scoped.

Motivo: introducir acceso relacional y DI progresivamente sin reemplazar todavía los endpoints ni añadir servicios o repositorios.

Consecuencia: el mapeo configura ID como clave identity by default, título obligatorio/máximo 100, descripción obligatoria y fecha timestamptz. La clase de dominio no necesita atributos ni propiedades nuevas. `IsRequired` configura nulabilidad del esquema; las reglas contra textos vacíos o espacios siguen siendo responsabilidad de la validación HTTP. Npgsql requiere fechas UTC al persistir timestamptz; los valores actuales ya se crean con UTC.

La cadena se obtiene de `ConnectionStrings:DefaultConnection`, mediante User Secrets en Development o `ConnectionStrings__DefaultConnection` en el entorno. Solo se versiona el identificador no sensible del almacén; no se han guardado credenciales reales. User Secrets no cifra los datos y se limita al desarrollo. El contexto comprueba que hay una cadena al resolverse; el registro no abre conexiones ni crea tablas; los GET lo solicitan ahora para consultar (decisión 014). No se llama a `EnsureCreated` ni `Migrate`; POST y PUT utilizan `SaveChangesAsync` desde las decisiones 015 y 016 respectivamente. La migración inicial está aplicada en `helpdesk_db`, verificada al cierre (decisión 013). Instalación/configuración local y etapas pendientes documentadas en `POSTGRESQL.md`.

### 013 — Primera migración InitialCreate

Estado: generada; aplicada posteriormente por el desarrollador y confirmada mediante consulta del historial al cierre de sesión.

Decisión: alinear la lectura de configuración con `ConnectionStrings:DefaultConnection` en User Secrets y fijar `dotnet-ef` 10.0.12 en el manifiesto local `dotnet-tools.json`. Generar `InitialCreate` mediante el contexto registrado en DI, con entorno Development, sin fábrica adicional.

Motivo: versionar el esquema inicial y permitir reproducir las herramientas, manteniendo secretos fuera del código.

Consecuencia: `Migrations` contiene la creación de `Tickets`, metadatos del modelo objetivo y la instantánea usada para futuras diferencias. `Up` define las cuatro columnas y su clave identity; `Down` describe la reversión. La generación no aplicó SQL ni modificó datos. Después el desarrollador aplicó `InitialCreate`; la revisión de cierre confirmó su registro en `__EFMigrationsHistory` de `helpdesk_db`. No se reaplicó ni revirtió la migración. Los GET, POST y PUT ya utilizan esa tabla; DELETE sigue en memoria.

### 014 — GET asíncronos con EF Core

Estado: implementada; POST y PUT migrados posteriormente (decisiones 015 y 016), DELETE pendiente.

Decisión: inyectar `HelpDeskDbContext` en las dos Minimal APIs GET. Obtener el listado con `ToListAsync` y buscar por ID con `FirstOrDefaultAsync`, usando `async`/`await` y manteniendo HTTP 200 o 404.

Motivo: aprender consultas asíncronas y DI sobre PostgreSQL de forma incremental.

Consecuencia inicial: GET consulta la tabla persistida y deja de leer los ejemplos en memoria. En esta etapa POST, PUT y DELETE conservaban su lista; la decisión 015 migra posteriormente POST. No se generan migraciones ni se modifica el modelo. Las pruebas reales confirmaron 200 con tabla vacía y 404 por ID inexistente; el caso de ID existente queda pendiente por falta de filas.

## Comprobaciones y pendientes históricos al cierre del 8 de octubre de 2026

Build correcto sin errores ni advertencias. GET del listado devolvió 200 con `[]`; GET por ID inexistente, 404 sin cuerpo. No se verificó la rama 200 por ID porque la tabla está vacía. La revisión de archivos versionados/no ignorados no detectó credenciales y staging estaba vacío.

La coexistencia de almacenes es temporal: POST devuelve una ubicación de GET, pero el ticket creado en memoria aún no existe en PostgreSQL. PUT y DELETE tampoco afectan la tabla. Pendiente migrar esas escrituras con operaciones asíncronas y `SaveChangesAsync` por tareas autorizadas, y eliminar la lista al completar esa transición. El cálculo actual `Max + 1` falla con lista vacía y puede reutilizar IDs eliminados; la generación identity está configurada en PostgreSQL pero POST todavía no la utiliza. La lista tampoco está sincronizada para concurrencia. Tests automatizados y futuras funcionalidades siguen pendientes.

### 015 — POST asíncrono con EF Core

Estado: implementada el 13 de octubre de 2026; sustituye la persistencia en memoria de la decisión 009.

Decisión: mantener POST en `Program.cs`, con `CreateTicketDto`, `ValidateTicket` e inyección de `HelpDeskDbContext`. Crear `Ticket` con Title, Description y fecha UTC sin asignar Id; registrar con `dbContext.Tickets.Add(ticket)` y guardar con `await dbContext.SaveChangesAsync()` antes de responder con `Results.Created`.

Motivo: aprender seguimiento de entidades, guardado asíncrono y generación de claves en PostgreSQL sin añadir capas.

Consecuencia: `Add` marca la entidad como Added; `SaveChangesAsync` ejecuta el INSERT y actualiza el ID del objeto con el generado por la columna identity existente. La respuesta conserva HTTP 201, el ticket y Location con su ID; los datos inválidos siguen devolviendo 400 antes de registrar o guardar. POST deja de depender de la lista y de `Max + 1`. El esquema no cambia y no se generan migraciones. En esta etapa PUT y DELETE conservaban la lista temporal; la decisión 016 migra posteriormente PUT. DELETE continúa pendiente.

Validación: build sin errores ni advertencias; nueve POST inválidos con 400 sin cambios; POST válido con 201 e ID 1 generado; ambos GET recuperan el ticket antes y después de reiniciar. Se dejó un único ticket de prueba en la tabla inicialmente vacía, sin eliminar datos. PostgreSQL conserva la fecha con precisión de microsegundos. `dotnet test` terminó con código 0, pero no hay proyectos de tests automatizados. Las comprobaciones HTTP requirieron autorización fuera del sandbox por el bloqueo de sockets.

### 016 — PUT asíncrono y seguimiento de cambios con EF Core

Estado: implementada el 13 de octubre de 2026; sustituye la persistencia en memoria de la decisión 011.

Decisión: conservar `UpdateTicketDto` e inyectar `HelpDeskDbContext` en PUT. Buscar por ID mediante `FirstOrDefaultAsync`, devolver 404 si no existe y validar con `ValidateTicket` antes de modificar. Cambiar únicamente Title y Description, guardar con `await dbContext.SaveChangesAsync()` y devolver 200 con el ticket actualizado.

Motivo: aprender cómo el contexto sigue una entidad consultada, detecta sus cambios y los persiste sin llamar a `Update` ni añadir servicios o repositorios.

Consecuencia: la consulta usa el seguimiento predeterminado. Al guardar, EF Core compara los valores originales con los actuales y ejecuta la actualización necesaria. Id y CreatedAt permanecen intactos; las entradas inválidas devuelven 400 sin modificar la entidad. El esquema no cambia y no se generan migraciones. GET y POST mantienen su implementación; DELETE conserva la lista temporal y es la única operación pendiente de migración.

Validación: build correcto sin errores ni advertencias; `dotnet test` con código 0, sin proyectos de tests. PUT válido devolvió 200; GET por ID y listado confirmaron los textos guardados con ID y fecha originales. Repetir PUT mantuvo el mismo resultado. Nueve entradas inválidas devolvieron 400 sin cambios y un ID inexistente devolvió 404 sin cuerpo, incluso con entrada inválida. Se creó y actualizó un ticket de prueba (ID 5), conservando los cuatro tickets previos. API detenida tras las pruebas HTTP autorizadas fuera del sandbox por el bloqueo de sockets. Sin commit ni push.

### 017 — Estado de tickets mediante enum y conversión a texto

Estado: implementada el 20 de octubre de 2026; migración aplicada posteriormente por el desarrollador, con seis tickets verificados en Open. La generación original no aplicó la migración.

Decisión: definir `TicketStatus` en Models con `Open`, `InProgress`, `Resolved` y `Closed`. Añadir `Ticket.Status = TicketStatus.Open`. Configurar `HasConversion<string>()` y `HasDefaultValue(TicketStatus.Open)` en el contexto. Generar `20261020092721_AddTicketStatus` sin modificar endpoints ni DTOs.

Motivo: aprender enums, conversores y evolución del esquema conservando tickets existentes, con estados legibles en PostgreSQL.

Consecuencia: Up añade exclusivamente `Status` como `text NOT NULL DEFAULT 'Open'`. Al aplicarse, las filas existentes recibirán `Open` sin recrear la tabla ni borrar registros; el inicializador cubre objetos nuevos y el default cubre inserciones que omitan la columna. Down elimina únicamente Status, perdiendo sus valores al revertir. Designer y snapshot reflejan el nuevo modelo. Renombrar estados en el futuro requerirá considerar los textos persistidos. No hay reglas de transición todavía. La conversión no cambia la serialización JSON: las respuestas con Ticket incorporarán status numérico con la configuración actual. El CRUD actualizado requiere aplicar previamente la migración; no se ha ejecutado database update ni SQL sobre la base.

Validación: build correcto (0 errores, 0 advertencias); dotnet test con código 0, sin proyectos de tests. Revisados migración, metadatos y SQL generado tras compilar. No se han repetido pruebas HTTP ni comprobado la migración aplicada sobre datos reales.

La revisión local también confirma DELETE persistido sin lista temporal; las cinco pruebas HTTP principales superadas fueron comunicadas por el desarrollador. Las referencias a DELETE pendiente en decisiones y cierres anteriores describen etapas históricas.

### 018 — Actualización parcial del estado mediante PATCH

Estado: implementada el 20 de octubre de 2026.

Decisión: crear `UpdateTicketStatusDto` con `string? Status` y registrar `PATCH /api/tickets/{id}/status`. Inyectar HelpDeskDbContext, consultar por ID y devolver 404 si no existe. Validar los nombres exactos de TicketStatus con Enum.IsDefined y convertir mediante Enum.Parse. Asignar únicamente Status, guardar con SaveChangesAsync y devolver 200 con Ticket.

Motivo: aprender actualización parcial, deserialización de DTOs y seguimiento de cambios, evitando que la entrada modifique otras propiedades.

Consecuencia: solo se aceptan Open, InProgress, Resolved y Closed con esa capitalización. Cadenas numéricas, combinaciones, textos desconocidos, null y campos ausentes devuelven 400. Un número JSON no se deserializa como string y produce 400 antes del handler; para DTOs deserializados la búsqueda precede a la validación. EF sigue la entidad consultada y persiste solo el estado modificado, usando el conversor a texto existente. Las respuestas conservan la serialización numérica del enum. No hay reglas de transición, cambios de esquema, nuevas migraciones ni capas. Los demás handlers permanecen intactos.

Validación: build con 0 errores y 0 advertencias; dotnet test con código 0, sin proyectos de tests. Pruebas HTTP reales: cuatro estados con 200 y GET posterior; ocho entradas inválidas con 400 y sin cambios; ID inexistente con 404. Conservadas las otras propiedades incluso enviando campos adicionales. Restaurado el estado original y comparados los seis tickets con el listado inicial. Sockets bloqueados en sandbox: pruebas completadas fuera de él con autorización y servidor detenido. No se repitieron POST, PUT ni DELETE. Sin commit ni push.

### 019 — Enums como texto en JSON

Estado: implementada el 20 de octubre de 2026.

Decisión: registrar `JsonStringEnumConverter` en las opciones HTTP JSON de las Minimal APIs mediante `builder.Services.ConfigureHttpJsonOptions`, antes de construir la aplicación.

Motivo: devolver estados legibles como `"Resolved"` en las respuestas, configurándolo una sola vez para todos los endpoints.

Consecuencia: la representación JSON es independiente del valor subyacente del enum C# y del conversor de EF Core para PostgreSQL. La configuración afecta a la serialización y deserialización de propiedades enum; el DTO de PATCH sigue usando string nullable, por lo que sus validaciones explícitas y el rechazo de números JSON permanecen intactos. No se cambian handlers, esquema, migraciones, dependencias ni capas.

Validación: build con 0 errores y 0 advertencias; dotnet test código 0 sin proyectos de tests. PATCH con los cuatro estados y GET posterior confirmaron nombres de texto. Ocho entradas inválidas, incluidas Inventado, número, null y ausente, devolvieron 400 sin cambios. ID inexistente: 404. Restaurado el estado original y comparados los seis tickets con el listado inicial. API de pruebas detenida; sin commit ni push.

### 020 — Respuestas breves para errores de enlace del cuerpo JSON

Estado: implementada el 20 de octubre de 2026.

Decisión: configurar RouteHandlerOptions.ThrowOnBadRequest=false y añadir UseStatusCodePages antes de los endpoints. Completar las respuestas 400 sin cuerpo con el JSON fijo `{"error":"El cuerpo JSON de la petición no es válido."}`.

Motivo: los DTOs se deserializan antes de entrar al handler; los números incompatibles con strings o JSON mal formado no llegan a su validación. Evitar que esos errores expongan detalles del servidor en Development mediante los mecanismos integrados de Minimal APIs.

Consecuencia: no hay captura general de excepciones. El framework produce 400 por errores de enlace y el middleware añade el mensaje; también cubre otros 400 vacíos de enlace, como parámetros de ruta incompatibles. Las respuestas de validación que ya tienen cuerpo se conservan. No se cambian otros códigos como 404 o 415, ni se convierten fallos de negocio o de PostgreSQL en 400. Development y los logs internos permanecen disponibles. Sin cambios en handlers, persistencia, esquema, migraciones o dependencias.

Validación: build con 0 errores y 0 advertencias; dotnet test código 0, sin proyectos de tests. HTTP en Development: PATCH numérico, mal formado, array, null y vacío, y POST/PUT incompatibles devolvieron exactamente el JSON genérico con 400. Inventado y demás validaciones conservaron su mensaje. Resolved devolvió 200 y GET confirmó texto. Restaurado el estado original y comparados los seis tickets por ID; 404 y health intactos. API detenida, sin commit ni push.

### 021 — Reglas explícitas de transición de estados

Estado: implementada el 20 de octubre de 2026; evoluciona la decisión 018, que inicialmente permitía cualquier cambio entre estados válidos.

Decisión: separar la tabla de transiciones en `Models/TicketStatusTransitions.cs`, clase estática con IsAllowed(currentStatus, requestedStatus), sin HTTP, EF Core ni servicios. Su switch permite Open → InProgress; InProgress → Resolved u Open; Resolved → Closed, InProgress u Open; Closed → Open. Otros cambios devuelven false. El endpoint resuelve por separado la igualdad como operación idempotente.

Motivo: expresar una máquina de estados sencilla y mantener las reglas de negocio independientes del transporte y la persistencia, sin introducir capas ni dependencias.

Consecuencia: PATCH conserva su DTO y validaciones 400/404. Si el estado coincide devuelve 200 con el ticket sin asignar ni guardar; si la transición no está permitida devuelve 409 con error, currentStatus y requestedStatus. Solo las transiciones permitidas modifican Status y llaman a SaveChangesAsync. Las respuestas conservan enums como texto y los errores JSON breves. No hay cambios de esquema ni migraciones. Los otros endpoints mantienen sus handlers. No se incorpora control de concurrencia entre peticiones simultáneas en esta etapa; las pruebas son secuenciales.

Validación: build con 0 errores y 0 advertencias; dotnet test código 0, sin proyectos de tests. HTTP real verificó las 16 combinaciones, los errores 400 y 404, persistencia mediante GET y conservación de las demás propiedades. Se restauró el estado original siguiendo las reglas y se compararon los seis tickets por ID. API detenida tras pruebas fuera del sandbox autorizadas; sin commit ni push.

### 022 — Primer proyecto de pruebas unitarias xUnit

Estado: implementada el 20 de octubre de 2026; inicia las pruebas automatizadas previstas en la decisión 005.

Decisión: añadir HelpDesk.Tests (net10.0) a la solución con ProjectReference a HelpDesk.Api. Usar xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 y xunit.runner.visualstudio 3.1.4, sin colector de cobertura, mocking ni infraestructura adicional. TicketStatusTransitionsTests llama directamente a la función pública IsAllowed mediante tres Theory con InlineData y Assert.True/False.

Motivo: verificar las reglas puras de negocio de forma rápida y aislada, sin servidor HTTP ni PostgreSQL.

Consecuencia: 16 casos cubren siete transiciones permitidas, cinco prohibidas y cuatro igualdades. IsAllowed devuelve false para estados iguales; la idempotencia HTTP pertenece al endpoint y no se prueba aquí. Referenciar la API permite compilar contra sus tipos públicos y no ejecuta Program. No se modifican reglas ni visibilidad de producción. Las pruebas de integración siguen pendientes. Las referencias anteriores a ausencia de tests describen etapas históricas.

Validación: build código 0, 0 errores y 1 advertencia MSB3277 por EF Core Relational transitivo 10.0.4/10.0.12; se registra para revisión específica sin alterar persistencia en esta tarea. dotnet test: 16 ejecutadas, 16 superadas, 0 fallidas, 0 omitidas. SDK 10.0.113. Ambos comandos completados fuera del sandbox con autorización por restricciones del entorno. Diff verificado; sin API arrancada ni acceso a PostgreSQL. Sin commit ni push.

### 023 — Referencia explícita a EF Core Relational

Estado: implementada el 20 de octubre de 2026; resuelve la advertencia registrada en la decisión 022.

Decisión: añadir Microsoft.EntityFrameworkCore.Relational 10.0.12 como dependencia directa de HelpDesk.Api, manteniendo EF Core y Design 10.0.12 y Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3.

Motivo: Design aporta Relational 10.0.12 solo al grafo de la API por PrivateAssets=all. En Tests, Npgsql resolvía la versión mínima 10.0.4 de su rango [10.0.4, 11.0.0), provocando MSB3277 al referenciar la API compilada contra 10.0.12. La referencia explícita de ejecución propaga la versión correcta a los consumidores de la API.

Consecuencia: ambos proyectos resuelven Relational 10.0.12, compatible con el rango de Npgsql y net10.0. No se añaden referencias EF en Tests, no se actualizan otros paquetes y no se suprimen advertencias. Sin cambios de negocio, endpoints, tests o migraciones.

Validación: restore y build correctos sin advertencias ni errores; test ejecutó 16 casos, 16 superados, 0 fallidos y 0 omitidos, sin advertencias. Grafo verificado con dotnet nuget why; diff correcto. Comandos fuera del sandbox con autorización por restricciones de MSBuild. Sin uso de PostgreSQL, commit ni push.

### 024 — Primera prueba HTTP con WebApplicationFactory

Estado: implementada el 20 de octubre de 2026; amplía la decisión 022 con integración HTTP sin base de datos.

Decisión: añadir Microsoft.AspNetCore.Mvc.Testing 10.0.12 en Tests y exponer Program mediante una declaración public partial al final del archivo de arranque. HealthEndpointTests crea una fábrica local por prueba, configura el entorno Testing y envía GET /api/health mediante el HttpClient de TestServer.

Motivo: comprobar conjuntamente arranque, enrutamiento y serialización de la API sin ejecutar manualmente Kestrel ni depender de infraestructura externa.

Consecuencia: la prueba verifica el contrato existente: 200, application/json y objeto con únicamente status=ok. TestServer ejecuta el pipeline en memoria, sin puerto HTTP. Health no resuelve HelpDeskDbContext; el registro diferido de sus opciones no configura ni conecta PostgreSQL en esta prueba, por lo que no se necesita sustituirlo. Las pruebas CRUD y cualquier configuración de base de datos de pruebas siguen pendientes. No se modifican handlers, migraciones ni reglas. Las 16 pruebas unitarias se conservan.

Validación: restore y build correctos con 0 errores y 0 advertencias; test con 17 superadas, 0 fallidas y 0 omitidas, sin advertencias. Comandos fuera del sandbox con autorización por restricciones de NuGet/MSBuild. Diff correcto; sin conexiones ni operaciones con PostgreSQL, commit o push.

### 025 — Integración PostgreSQL aislada y activación por filtro

Estado: infraestructura y tres pruebas implementadas y compiladas; validación sobre PostgreSQL pendiente de configuración manual.

Decisión: implementar en HelpDesk.Tests/Integration una fábrica específica, fixture xUnit y validador de conexión. Exigir HELPDESK_TEST_CONNECTION_STRING sin fallback, base exacta helpdesk_test_db, usuario exclusivo helpdesk_test_user, contraseña explícita y host local. Rechazar opciones fuera de una lista cerrada antes de conectar. Comprobar mediante SELECT la base y usuario efectivos y que el rol no tenga SUPERUSER, CREATEDB o CREATEROLE antes de MigrateAsync. Mantener abierta esa conexión de verificación durante la preparación. Base/rol se crean manualmente; no se usan conexiones administrativas ni EnsureCreated/EnsureDeleted.

Motivo: probar con el motor y migraciones reales sin escribir en helpdesk_db, separando credenciales y evitando depender de secretos de desarrollo. El rol debe carecer también de permisos de escritura sobre desarrollo y de membresías privilegiadas; preparación manual descrita en POSTGRESQL_TESTS.md.

Consecuencia: ConfigureTestServices sustituye HelpDeskDbContext, DbContextOptions y su IDbContextOptionsConfiguration original, exclusivamente en Testing. La fábrica usa TestServer y suprime logs de proveedor; los errores propios no incluyen secretos. Cada instancia de prueba genera un GUID para su título y limpia únicamente sus filas mediante DisposeAsync y DELETE filtrado. No hay resets globales ni reinicio de secuencias. Interrupciones o pérdida de conexión pueden dejar filas; no se garantiza limpieza tras abortar el proceso. No coordina migraciones de procesos distintos. Sin cambios de API, esquema versionado o dependencias.

Ejecución: las pruebas PostgreSQL tienen Trait Category=PostgreSQL. El comando con filtro Category!=PostgreSQL mantiene los tests actuales sin base configurada; solicitar PostgreSQL o ejecutar todo sin filtro falla claramente si falta configuración. No se oculta esa ausencia mediante skip o conexión alternativa.

Validación real: restore/build correctos sin errores ni advertencias. Pruebas sin PostgreSQL: 25 superadas (17 existentes y ocho del validador), 0 fallidas. No existe configuración de pruebas en esta sesión; no se conectó a PostgreSQL ni se ejecutaron los tres casos HTTP PostgreSQL. Quedan pendientes la preparación manual, verificación del destino y ejecución. Diff correcto; sin commit ni push.

### 026 — Validación repetible de la conexión de pruebas

Estado: corregida el 20 de octubre de 2026.

Decisión: permitir Include Error Detail, Persist Security Info y Enlist únicamente con false, además de la lista cerrada original. Mantener la validación tanto en la fixture como en el constructor de TicketApiFactory. Separar errores de sintaxis, claves no admitidas, valores inválidos y restricciones de seguridad sin revelar configuración ni inner exceptions.

Motivo: una prueba reprodujo que Validate añadía esas tres claves y luego rechazaba su propia salida cuando la fábrica la validaba otra vez. No es necesario eliminar controles ni cambiar el flujo de conexión para resolverlo.

Consecuencia: la normalización es repetible; siguen siendo obligatorios helpdesk_test_db, helpdesk_test_user, contraseña explícita y servidor local. Pruebas con NpgsqlConnectionStringBuilder verifican contraseñas ficticias especiales y las opciones inseguras siguen rechazadas. Sin cambios de fixture, fábrica, API, esquema ni paquetes.

Validación: regresión reproducida antes de corregir; después restore/build sin errores ni advertencias y 39 pruebas sin PostgreSQL superadas. Variable real ausente en el proceso del agente: contraseña no inspeccionada y tres pruebas PostgreSQL no ejecutadas. No se asume una causa adicional en la configuración del usuario. Diff correcto; sin commit ni push.

### 027 — Integración de PUT, PATCH y DELETE con aislamiento por prueba

Estado: 17 casos nuevos implementados y compilados, sin ejecución PostgreSQL en esta sesión por configuración ausente.

Decisión: ampliar TicketEndpointsTests reutilizando sin cambios la fixture, fábrica y validador PostgreSQL. Preparar tickets específicos con GUID mediante scopes EF y recargarlos para comparar con la precisión real de PostgreSQL. Verificar cada efecto mediante GET y contexto independiente AsNoTracking. Usar un segundo ticket propio como control al probar DELETE. Mantener los casos en la colección de la clase existente.

Motivo: cubrir persistencia y preservación de propiedades en PUT/PATCH, rechazo de entradas/transiciones y eliminación selectiva, sin modificar contratos ni reglas para adaptar los tests.

Consecuencia: registrar títulos GUID antes de guardar y los IDs propios después permite limpieza limitada a esos registros incluso cuando PUT cambia el título o falla una aserción. El POST previo mantiene la limpieza por su título original si falla antes de leer el ID. No se comparan ni borran conjuntos globales que puedan interferir con otras ejecuciones. No hay nuevas dependencias ni cambios de seguridad, esquema o API. Interrupciones o fallos de conexión pueden impedir la limpieza; no se amplía la garantía a procesos abortados.

Validación: restore/build correctos, 0 errores y 0 advertencias; 39 tests sin PostgreSQL superados. Descubiertos 20 casos PostgreSQL, tres previos más 17 nuevos, mediante listado sin ejecución. Variable ausente en este proceso: no se conecta ni se ejecuta PostgreSQL; verificación funcional pendiente. Diff correcto; sin commit ni push.

### 028 — Prioridad independiente y evolución aditiva del esquema

Estado: implementada el 20 de octubre de 2026; migración generada y revisada, no aplicada en esta sesión.

Decisión: definir TicketPriority con Low, Medium, High y Critical y añadir Ticket.Priority=Medium. Configurar HasConversion<string>(), HasDefaultValue(Medium) y HasSentinel(Medium). Compartir una función pura de validación de nombres exactos entre POST y el nuevo PATCH de prioridad. DTOs mantienen strings para rechazar números JSON durante el enlace y textos numéricos mediante validación explícita.

Motivo: expresar urgencia independientemente de la fase del ticket, conservar compatibilidad de creación y evolucionar el esquema sin perder datos. El sentinel explícito garantiza que Low (valor cero del enum) se trate como valor real de inserción, no como ausencia que active el default Medium.

Consecuencia: POST acepta Priority opcional, con Medium al omitirla; null explícito es inválido. PATCH /api/tickets/{id}/priority busca primero el ticket, devuelve 404 si no existe, 400 por prioridad inválida, y 200 sin asignar ni guardar si coincide. Para un cambio válido asigna solo Priority y guarda con EF. No hay restricciones entre prioridades. PUT conserva Priority y no cambian las reglas de Status. El conversor JSON central devuelve los nombres de ambos enums.

Migración 20261020105220_AddTicketPriority: Up solo añade Priority text NOT NULL DEFAULT 'Medium' en Tickets. Al aplicarse, las filas anteriores reciben Medium sin borrar ni recrear la tabla; Down solo elimina la columna y pierde sus valores. Snapshot y Designer actualizados. No se aplica sobre desarrollo automáticamente. El esquema anterior debe migrarse tras revisión antes de usar el modelo actualizado.

Pruebas: 15 casos nuevos sin base para nombres válidos/invalidos, defaults y metadatos EF; 29 casos PostgreSQL de POST/PATCH, JSON, idempotencia, 404 y conservación de campos. Se agrupan en otro archivo de la misma clase parcial de integración, reutilizando fixture, validación y limpieza por GUID/ID. Las comparaciones previas también incluyen Priority. No hay nuevas dependencias ni cambios de seguridad.

Validación real: restore/build sin errores ni advertencias; 54 pruebas sin PostgreSQL superadas, 0 fallidas. Descubiertos 49 casos PostgreSQL sin ejecutarlos por variable ausente. El desarrollador comunica que los 59 casos previos pasaron antes de esta etapa. SQL aditivo revisado sin ejecutar; migración y pruebas de persistencia pendientes de aplicación/ejecución en entorno seguro. Diff correcto; sin commit ni push.

### 029 — Categorías con enum y migración aditiva

Estado: implementada el 20 de octubre de 2026; migración generada/revisada, aplicación y pruebas PostgreSQL pendientes en esta sesión.

Decisión: TicketCategory define Hardware, Software, Network, Access y Other. Ticket.Category se inicializa en Other y el contexto configura conversión string, default Other y sentinel Other. La función pura TicketCategoryValidation.TryParse valida nombres exactos y se comparte entre POST y PATCH, siguiendo el patrón de prioridades sin añadir capas o paquetes.

Motivo: clasificar el tipo de incidencia independientemente de estado y prioridad, preservar clientes que omiten categoría y conservar los tickets previos al evolucionar el esquema. El sentinel evita confundir Hardware (valor cero) con ausencia de valor al insertar.

Consecuencia: CreateTicketDto acepta Category como texto opcional inicializado en Other; null explícito se rechaza. PATCH recibe UpdateTicketCategoryDto, busca primero por ID, devuelve 404 si no existe, valida con 400 y devuelve 200 sin escritura si coincide. Solo Category cambia y se guarda cuando corresponde; no hay transiciones entre categorías. El conversor JSON existente conserva nombres de enums. Status, Priority, textos y fecha no se alteran por este PATCH.

Migración 20261020110201_AddTicketCategory: Up solo añade Category text NOT NULL DEFAULT 'Other' a Tickets, asignando Other a los registros anteriores al aplicarse. No hay eliminación/recreación de tabla ni cambios a migraciones previas. Down elimina solo la columna y pierde sus valores. Snapshot y Designer actualizados. No se aplica automáticamente en desarrollo; la API actualizada necesita el esquema migrado tras revisión.

Validación: restore/build sin errores ni advertencias; 70 casos sin PostgreSQL superados (16 nuevos). Descubiertos 81 PostgreSQL (32 nuevos) con --list-tests sin ejecución, por variable de pruebas ausente. Pruebas preparadas de POST, PATCH, JSON, idempotencia, GET y preservación de propiedades con la infraestructura y limpieza existentes. SQL revisado sin ejecutar. No se accede a helpdesk_db; diff correcto, sin commit ni push.

### 030 — Listado filtrado y paginado mediante IQueryable

Estado: implementada el 21 de octubre de 2026; verificación PostgreSQL pendiente por configuración ausente.

Decisión: mantener el endpoint en Program.cs y definir TicketPageDto como contrato de salida. Validar parámetros conocidos y duplicados antes de ejecutar SQL. Aceptar enums con nombres exactos, page positivo y pageSize entre 1 y 100; calcular offset en long y rechazar valores superiores a int.MaxValue para usar Skip de forma segura. No se cambia el esquema ni se crean migraciones.

Motivo: componer consultas LINQ diferidas para que PostgreSQL filtre y limite las filas transferidas, sin materializar el conjunto completo. AsNoTracking evita seguimiento innecesario en el listado.

Consecuencias: filtros combinables, búsqueda literal mediante ILIKE con escape de comodines y parámetros EF; CountAsync previo a OrderByDescending(CreatedAt), ThenByDescending(Id), Skip y Take. TotalPages usa aritmética long y vale cero sin resultados. TicketPageDto conserva tickets completos. Se rompe únicamente el contrato del listado anterior en array; los demás endpoints y reglas permanecen intactos. Conteo y página son dos consultas sin transacción de snapshot; escrituras concurrentes pueden cambiar totales o desplazar filas entre peticiones. La ordenación estable resuelve empates en datos sin cambios.

Pruebas HTTP en TicketEndpointsTests.List.cs, en la misma clase parcial, fixture y limpieza por IDs/títulos propios. No se cambian las protecciones de helpdesk_test_db ni las migraciones existentes.

### 031 — Usuarios independientes y correo canónico único

Estado: implementada el 21 de octubre de 2026; AddUsers generada/revisada sin aplicar. Pruebas PostgreSQL pendientes por configuración ausente.

Decisión: User contiene Id identity by default, Name requerido varchar(100), Email requerido varchar(254), Role convertido a texto con default y sentinel User, y CreatedAt timestamptz. UserRole define User, Technician y Admin. El objeto inicia CreatedAt con DateTime.UtcNow; Npgsql rechaza fechas no UTC al escribir. DbSet<User> y Fluent API se añaden al contexto existente sin capas, paquetes ni relación con Ticket.

Email se normaliza al asignar con Trim y ToLowerInvariant. Se combina un índice único convencional sobre Email con CHECK Email = lower(btrim(Email)): el motor exige la representación canónica, también para escrituras ajenas a EF. SQL directo con mayúsculas se rechaza, no se transforma. El índice protege ante concurrencia; una comprobación previa de existencia no lo sustituye. Se descarta la extensión citext para evitar un requisito adicional y mantener varchar(254). La política considera insensible a mayúsculas toda la dirección, incluida la parte local; se pierde la grafía original. No valida formato ni equivalencias específicas de proveedores. Futuras consultas por igualdad deben normalizar su parámetro. Revisar el comportamiento de lower y la configuración regional de PostgreSQL si se van a admitir direcciones internacionalizadas: .NET y PostgreSQL tienen reglas de capitalización propias. No se garantiza equivalencia de todas las representaciones Unicode.

IsRequired implica NOT NULL, no validación de strings vacíos o espacios. Esta etapa configura persistencia; la validación de entrada se definirá al crear endpoints. No se añaden restricciones de valores de Role en SQL, siguiendo el patrón de los enums de tickets.

AddUsers.Up crea exclusivamente Users (PK y CHECK incluidos) y IX_Users_Email único. Down elimina únicamente Users; revierte el esquema perdiendo los usuarios que contenga. El SQL generado incluye el registro convencional en __EFMigrationsHistory. No contiene ALTER/DROP/UPDATE/DELETE sobre Tickets ni datos iniciales. Snapshot y Designer incorporan solo User, manteniendo el mapeo de Ticket.

Pruebas sin base inspeccionan metadatos, roles, normalización, defaults y operaciones de migración. Los casos PostgreSQL están en la clase parcial TicketEndpointsTests.Users.cs para compartir la fixture existente sin preparaciones de esquema paralelas. Limpieza por correos GUID exactos e IDs de entidades propias, y limpieza de tickets en finally. Infraestructura y validación de seguridad sin cambios; no se usa helpdesk_db.

### 032 — Endpoints de usuarios con DTOs y control de unicidad concurrente

Nota de evolución: POST/PUT públicos sustituidos por 403 en la decisión 035; se conserva aquí el diseño histórico.

Estado: implementada el 21 de octubre de 2026; verificación HTTP PostgreSQL pendiente en esta sesión por conexión ausente.

Decisión: mantener Minimal APIs en Program.cs con HelpDeskDbContext. CreateUserDto y UpdateUserDto delimitan Name, Email y Role; UserResponseDto delimita Id, Name, Email, Role y CreatedAt. Los GET proyectan DTOs en consultas AsNoTracking; el listado ordena por Id ascendente y devuelve array sin paginación. POST genera fecha UTC y devuelve 201/Location. PUT consulta primero el ID (404 si falta), valida y asigna solo campos editables, conservando Id/CreatedAt.

UserValidation es una función compartida y comprobable sin base. Name obligatorio/no whitespace, longitud original máxima 100, texto conservado. Email se recorta y pasa a minúsculas invariantes antes de comprobar longitud máxima 254 y formato. MailAddress.TryCreate debe devolver la misma dirección, sin display name ni whitespace. Dominio con al menos dos etiquetas ASCII, límites de 63 caracteres, sin guiones extremos; se admite punycode, no dominios Unicode sin convertir. No se comprueba existencia/entrega ni equivalencias por proveedor. Role usa nombres exactos del enum, sin aceptar números; POST inicializa User si se omite y PUT exige Role explícito. Null explícito se rechaza. JSON incorrecto utiliza el tratamiento central ya existente.

Un AnyAsync sobre Email normalizado permite anticipar 409; PUT excluye su propio ID. El índice único existente sigue siendo la garantía ante concurrencia. Se captura únicamente DbUpdateException cuya InnerException sea PostgresException con SqlState=23505 y ConstraintName=IX_Users_Email; devuelve 409 con mensaje fijo, sin datos internos. No se convierten otros errores en conflictos. PUT no escribe nada si falla la validación o la consulta previa; la escritura que falla por unicidad se revierte por PostgreSQL.

Pruebas: unitarias de validación y defaults de DTOs; HTTP PostgreSQL en la misma clase parcial/fixture con correos GUID y limpieza propia. Un SaveChangesInterceptor exclusivo de dos tests sincroniza las solicitudes después de AnyAsync para comprobar las carreras POST/POST y PUT/PUT contra el índice real, con un límite temporal para detectar fallos de sincronización. Se añade a una fábrica derivada de la ya validada sin cambiar destino, logs ni controles de seguridad. Se comprueba persistencia y conservación del usuario que pierde la carrera.

Sin cambios de modelo, contexto, migraciones, paquetes, endpoints/contrato de tickets ni relaciones. Sin autenticación, DELETE de usuarios o contraseñas. Los roles son editables por estos endpoints durante esta etapa; el control de acceso queda para una tarea posterior. El listado completo no tiene límite de tamaño todavía.

### 033 — Relaciones opcionales de creador y técnico asignado

Estado: implementada el 21 de octubre de 2026; AddTicketUserRelations generada/revisada sin aplicar. Verificación PostgreSQL pendiente en esta sesión por conexión ausente.

Decisión: Ticket incorpora CreatedByUserId y AssignedToUserId como int? y navegaciones CreatedByUser/AssignedToUser opcionales. Fluent API configura dos HasOne/WithMany independientes, con índices no únicos y DeleteBehavior.Restrict. No se añaden colecciones inversas a User porque todavía no se utilizan. JsonIgnore en las navegaciones conserva respuestas de tickets basadas en entidad, añadiendo solo los dos IDs públicos, sin objetos de usuario ni ciclos. El sobre paginado no cambia.

Motivo: preservar tickets y clientes anteriores mediante null, representar autoría y responsabilidad independientemente y dejar la integridad de existencia a PostgreSQL. ON DELETE RESTRICT impide borrar un usuario con referencias persistidas sin resolverlas primero; nunca se borran tickets en cascada. No se crea endpoint DELETE de usuarios.

POST de tickets recibe CreatedByUserId opcional y comprueba existencia para cualquier rol. Ausente/null es válido, inexistente devuelve 400. AssignedToUserId no se acepta en creación. PUT de tickets sigue limitado a textos; PATCH de estado/prioridad/categoría no cambia las relaciones. PATCH assignment usa UpdateTicketAssignmentDto con JsonRequired: campo ausente o tipo incorrecto produce 400, null explícito desasigna. Tras buscar el ticket (404), valida usuario y rol Technician/Admin (400), asigna solo el FK y guarda; repetir un destino válido no guarda. No añade restricciones por estado.

POST de tickets y PATCH assignment convierten únicamente 23503 de FK_Tickets_Users_CreatedByUserId o FK_Tickets_Users_AssignedToUserId en 409 con texto fijo, para una eliminación concurrente tras la comprobación previa. No se capturan errores de infraestructura ni otras restricciones como si fueran validación.

PUT de usuarios rechaza Role=User con 409 cuando existen tickets asignados, antes de asignar cualquier campo. Ser creador no impide cambiar el rol; Technician y Admin siguen siendo destinos válidos con asignaciones. Se incorpora esta comprobación por la nueva regla de asignación, manteniendo DTO y demás respuestas.

Límite de la implementación inicial, sustituido por la decisión 034: los chequeos de rol y asignaciones no comparten bloqueo/transacción entre endpoints. Una degradación de rol y asignación concurrentes pueden superar ambos chequeos y persistir User con tickets asignados. FK no valida roles y SQL directo también evita las reglas HTTP. Un test con interceptor pausa la asignación después de validarla, permite el cambio de rol y constata esa limitación; no se presenta como garantía resuelta. Resolverla requeriría una decisión posterior sobre bloqueo común/aislamiento o reglas en base. La asignación concurrente del mismo ticket no tiene token de concurrencia; prevalece la última escritura.

Migración 20261021120235_AddTicketUserRelations: Up solo añade dos integer NULL, índices y FK RESTRICT a Tickets. No altera columnas anteriores, actualiza filas, crea usuarios, borra o reconstruye tablas. Down retira FK/índices/columnas nuevas; pierde relaciones al revertir pero conserva el resto del ticket. SQL incremental desde AddUsers revisado sin ejecutar. La API actualizada necesita aplicar el esquema antes de arrancar; el DDL toma bloqueos de tabla.

Pruebas de modelo, serialización y operaciones de migración sin conexión. Integración en la clase parcial existente con scopes y datos GUID propios, incluido el rechazo SQL de usuarios inexistentes/borrado referenciado, carreras de eliminación y límite concurrente de rol. La limpieza cambia de orden: tickets propios primero, usuarios propios después (finally), respetando las FK. Fixture, fábrica base, validador y destino seguro sin cambios. No se garantiza limpieza si el proceso se interrumpe o la conexión falla.

### 034 — Exclusión entre asignación y cambio de rol en PostgreSQL

Nota de evolución: PUT público deshabilitado por la decisión 035; assignment conserva su transacción y FOR SHARE. El protocolo de PUT se recupera con autorización Admin en la decisión 037.

Estado: solución local revisada el 21 de octubre de 2026; compilada, ejecución PostgreSQL pendiente por conexión de pruebas ausente.

Decisión: transacciones explícitas ReadCommitted en asignación a un usuario y PUT de usuarios. PATCH obtiene FOR SHARE sobre la fila del destino mediante SQL parametrizado y lee su rol en esa misma consulta. PUT obtiene FOR NO KEY UPDATE al leer el usuario; consulta después las asignaciones y conserva 409 si se solicita User con tickets asignados. Ambos mantienen el bloqueo hasta commit o disposición de la transacción. Desasignar solo necesita la escritura atómica habitual de EF Core.

Motivo: los chequeos previos independientes permitían que ambas operaciones validaran antes de persistir y produjeran User con un ticket asignado. Los dos modos de bloqueo son incompatibles, por lo que el segundo solicitante valida después de la confirmación del primero. ReadCommitted permite a la consulta posterior de asignaciones observar el commit que desbloqueó al PUT. FOR KEY SHARE no bastaría: permite actualizaciones del rol.

Consecuencias: orden de bloqueo de filas usuario → escritura del ticket; PUT consulta tickets sin bloquear sus filas. No se serializan asignaciones a tickets distintos del mismo usuario ni operaciones de usuarios independientes. La fila del usuario se bloquea también en PUT que modifica otros campos para evitar conversiones posteriores de bloqueo. No se añaden bloqueos explícitos de tabla, reintentos, triggers, paquetes ni migraciones. Se conservan contratos y errores HTTP. SQL ajeno a estos endpoints no queda sujeto a esta regla de rol.

Pruebas: doce casos concurrentes para ambos órdenes, roles válidos, asignación/reasignación, independencia de usuarios, compatibilidad de asignaciones y borrado restringido. Sincronización con interceptores y TaskCompletionSource; observación de pg_blocking_pids para demostrar espera real, sin Task.Delay. Compiladas y descubiertas, sin ejecución PostgreSQL en esta sesión. Las 120 pruebas sin PostgreSQL sí se ejecutaron y superaron.

### 035 — Credenciales nullable, registro User y retirada de gestión pública

Estado: implementada el 21 de octubre de 2026; migración generada/revisada sin aplicar. PostgreSQL pendiente por conexión de pruebas ausente.

Decisión: User.PasswordHash string? con JsonIgnore y columna text NULL. PasswordHasher<User> V3 del framework compartido, registrado por DI mediante IPasswordHasher<User>, con 210.000 iteraciones PBKDF2-HMAC-SHA512. No hay paquetes nuevos, modelo completo de Identity ni criptografía propia. La política valida 15–128 unidades UTF-16, no whitespace exclusivo ni controles, sin transformar la contraseña. PasswordHash no forma parte de ningún DTO público.

POST /api/auth/register reutiliza UserValidation con rol fijo User, normaliza email y genera hash/fecha/ID en servidor. Su DTO solo acepta Name, Email y Password; campos adicionales no alteran el modelo. Devuelve 201/Location/UserResponseDto, 400 por entrada inválida y 409 por duplicado. La consulta previa y el índice único IX_Users_Email cubren también concurrencia mediante la captura acotada de 23505.

Por ausencia de autorización, POST/PUT /api/users quedan incondicionalmente deshabilitados con 403 antes de resolver el contexto o leer el cuerpo, en todos los entornos. Se retiran los handlers anteriores; no hay clave fija ni bypass de desarrollo/pruebas. GET y tickets conservan sus contratos y bloqueos. Cuando vuelva la edición autorizada de roles deberá conservarse el protocolo de concurrencia 034.

AddUserPasswordHash solo añade una columna nullable sin default/backfill. No modifica Tickets, relaciones ni filas anteriores. Down perdería los hashes al eliminar la columna. Las cuentas anteriores siguen sin credencial: el futuro login debe rechazar hash null/vacío. La primera contraseña requerirá activación con titularidad verificada, token de un uso y caducidad; no se implementa ese procedimiento ahora ni se permite reclamar cuentas repitiendo registro. Detalle y revisión previa en AUTHENTICATION.md.

Pruebas: 17 casos nuevos sin PostgreSQL y 39 PostgreSQL (registro, validación, roles, hash/verificación, privacidad y logs, duplicados concurrentes con barrera, bloqueo de gestión y preservación). Las pruebas de los antiguos handlers públicos se sustituyen por el contrato actual, sin skips ni activación especial en Testing. La prueba de migración aplica el DDL generado sobre tablas temporales privadas con datos anteriores y FK, sin revertir el esquema compartido. Fixture, fábrica base y validador de conexión segura permanecen intactos. Suite actual: 137 sin PostgreSQL ejecutadas/superadas y 217 PostgreSQL solo descubiertas.

## Mantenimiento del registro

Ante un cambio arquitectónico importante, registra la decisión, su estado, motivos y consecuencias. Si reemplaza una decisión previa, conserva el motivo de la evolución y actualiza el estado anterior. Refleja también el avance en `PROGRESS.md`.


### 036 — Login y autenticación JWT sin autorización (etapa 9.2)

Estado: implementada el 23 de octubre de 2026; validación HTTP PostgreSQL pendiente por conexión segura ausente.

Decisión: POST /api/auth/login verifica la contraseña mediante el hasher existente y consulta por correo canónico con AsNoTracking. Devuelve un 401 genérico para cuenta inexistente, hash ausente o contraseña incorrecta. No escribe ni actualiza hashes. LoginDto limita la entrada a email/password y LoginResponseDto limita la salida al token, tipo y duración.

JwtTokenIssuer, registrado mediante DI, emite HS256 con ID, email, rol almacenado, issuer, audience, iat, nbf y exp. TimeProvider permite comprobar fechas sin esperas. JwtSettings y su IValidateOptions validan al arrancar issuer/audience y clave externa Base64 de al menos 32 bytes; se rechazan patrones triviales, sin confundir esa heurística con garantía de entropía. No hay defaults, claves efímeras de arranque ni secretos versionados.

Se añade Microsoft.AspNetCore.Authentication.JwtBearer 10.0.12. El handler estándar valida firma, algoritmo HS256, emisor, audiencia y caducidad con 30 segundos de tolerancia. UseAuthentication prepara el principal sin restringir los endpoints; no se añade autorización por roles ni rutas privadas. POST/PUT de usuarios siguen bloqueados. Un manejador global evita respuestas de excepción con detalles sensibles también en Development; los errores 400 mantienen su contrato.

Motivo: aprender emisión y validación de credenciales Bearer con las opciones y servicios de ASP.NET Core, separando autenticación de autorización y conservando una estructura pequeña.

Consecuencias: configuración JWT obligatoria incluso para health; ningún cambio de esquema ni migración. Los JWT no están cifrados ni se revocan automáticamente ante cambios posteriores de rol/contraseña o futura deshabilitación. No hay refresh tokens, recuperación ni limitación de intentos; la autorización y revocación quedan pendientes. Se mantienen diferencias temporales entre cuenta ausente y hash verificable. Procedimiento Linux Mint sin imprimir secretos y límites en AUTHENTICATION.md.

Las fábricas de pruebas inyectan opciones efímeras solo por ConfigureTestServices; no existe bypass en la aplicación. Las pruebas Bearer verifican el handler real por AuthenticateAsync sin añadir un endpoint público de perfil. Las pruebas HTTP de login reutilizan exclusivamente helpdesk_test_db y la limpieza propia existente. Resultados ejecutados en PROGRESS.md.

### 037 — Políticas de cuenta actual y acceso a recursos (etapa 9.3)

Estado: implementada el 23 de octubre de 2026; ejecución PostgreSQL pendiente por conexión segura ausente. Evoluciona 032/034/035/036: se reactiva la administración de usuarios exclusivamente para Admin con el protocolo de concurrencia existente.

Decisión: AddAuthorization con DefaultPolicy/FallbackPolicy que requieren autenticación y CurrentAccountRequirement; políticas Admin y ChangeStatus añaden los roles permitidos. Todas las rutas llevan metadatos explícitos y solo health/register/login son anónimas. UseAuthorization sigue a UseAuthentication. No hay esquema alternativo ni bypass de Testing.

CurrentAccountHandler scoped exige sub único entero positivo y role único reconocido. Consulta la existencia y rol actual en PostgreSQL y exige coincidencia con el token antes de completar CurrentAccount. La verificación ocurre una vez por petición protegida; no se reutilizan datos entre peticiones. JWT ausente/inválido/caducado produce 401 con challenge Bearer; cuenta ausente o rol cambiado produce 403 sin explicar cuál de esas condiciones falló. Una avería de PostgreSQL falla de forma cerrada con 500 genérico. El handler resuelve DbContext después de comprobar la autenticación para evitar consultas con peticiones anónimas.

TicketAccess centraliza visibilidad por propietario/asignado mediante IQueryable: User solo creados por él, Technician creados por él o asignados a él, Admin todos. Se aplica antes de contar/paginar y en detalle (404 si es invisible). Los históricos con creador NULL quedan para Admin o Technician asignado. POST fija el creador a la cuenta autenticada e ignora el valor enviado por cualquier rol. PUT, DELETE, prioridad, categoría y asignación requieren Admin. Estado requiere Admin o Technician asignado; un ticket propio visible pero no asignado al técnico da 403, invisible da 404.

Estado obtiene FOR UPDATE sobre el ticket dentro de ReadCommitted antes de comprobar el recurso y conserva el bloqueo hasta finalizar, evitando usar una asignación anterior a una reasignación concurrente. Assignment mantiene usuario FOR SHARE antes de guardar el ticket; PUT de usuarios recupera usuario FOR NO KEY UPDATE antes de consultar asignaciones, guardando al final de ReadCommitted. Se conserva el orden usuario → ticket en estas dos operaciones. Las pruebas de ambos órdenes de cambio de rol/asignación y de reasignación/estado observan bloqueos reales mediante pg_blocking_pids, sin temporizadores usados como sincronización.

GET de usuarios requiere Admin; GET por ID permite también el perfil propio y oculta otros con 404. POST/PUT administrativos reutilizan DTOs, validación y captura acotada de unicidad. POST crea sin credencial; PUT conserva hash/ID/fecha y rechaza cambiar a User con asignaciones. No hay bootstrap ni activación de Admin: si no existe una cuenta privilegiada con credencial válida, la administración queda inaccesible hasta una etapa de aprovisionamiento seguro solicitada por separado. No se modifican cuentas existentes para eludir esa limitación.

Motivo: separar autenticación, permisos por rol y acceso a cada recurso, evitando comprobaciones dispersas o confiar solo en roles obsoletos firmados. Se mantiene la estructura sencilla: dos archivos en Authorization, sin nuevas dependencias, capas, modelos ni migraciones.

Consecuencias: las peticiones protegidas consultan PostgreSQL incluso para lecturas. El chequeo de rol no cancela operaciones ya autorizadas que estén en curso, ni es una revocación permanente del JWT: si el rol vuelve a su valor anterior antes de expirar, el token puede volver a autorizarse. No hay revocación por cambio de contraseña, deshabilitación, rate limiting ni prevención de auto degradación del último Admin. Operaciones externas SQL pueden eludir las reglas de rol; se mantienen las limitaciones de última escritura para gestión administrativa concurrente de tickets. Alcance pendiente documentado en AUTHENTICATION.md.

Los tests anteriores de validación, persistencia y filtros utilizan ahora un Admin propio persistido por caso y JWT firmado real. Registro/login siguen anónimos; los tests nuevos verifican User/Technician/Admin y negaciones con las políticas reales, sin ocultar regresiones mediante skip. Fixture/validador de destino exclusivo helpdesk_test_db intactos; solo los helpers HTTP de las pruebas se adaptan. Resultados realmente ejecutados y limitaciones en PROGRESS.md.


### 038 — Primer Admin mediante consola interactiva y bloqueo de Users (etapa 9.4)

Estado: implementada el 23 de octubre de 2026; cierre funcional confirmado por el usuario el 27 de octubre de 2026 con creación real del primer Admin, login y acceso administrativo. La suite completa está confirmada por el usuario: 538 pruebas superadas, 0 fallidas; véase PROGRESS.md. La recuperación puntual mediante una herramienta temporal externa no añade una capacidad permanente ni cambia esta decisión.

Decisión: añadir HelpDesk.AdminBootstrap (net10.0) como consola separada, con referencia a HelpDesk.Api para reutilizar User/UserRole, DbContext y validadores. La API no referencia ni ejecuta la consola; su punto de entrada explícito BootstrapProgram no arranca un host, no carga JWT/User Secrets/appsettings ni llama a Migrate/EnsureCreated. No hay endpoints de bootstrap ni cambios a la autorización existente.

Extraer el registro del hasher a Authentication/PasswordHashing.cs, utilizado por ambos ejecutables: mismos 210.000 ciclos, V3 y PasswordHasher<User>. La CLI acepta exclusivamente una conexión externa dedicada, exige terminal sin redirección, pide email/nombre y contraseña oculta dos veces, y valida como registro. No permite credenciales por argumentos. BootstrapConnection no tiene ToString con campos secretos; filtra opciones de conexión, fija public y desactiva diagnósticos sensibles. Solo muestra host/puerto/base/usuario y operación. El operador escribe la base exacta y CONFIRMAR PRIMER ADMIN antes de conectar/escribir; el servicio comprueba también database/user efectivos.

BootstrapService calcula el hash antes del bloqueo, abre ReadCommitted, toma LOCK TABLE public.Users IN SHARE ROW EXCLUSIVE MODE antes de consultar roles/correo y mantiene el bloqueo hasta commit/rollback. Rechaza cualquier Admin, incluso sin hash, y correo existente; no promueve ni activa cuentas anteriores. Solo inserta un Admin nuevo con hash. El modo es autoconflictivo y excluye escrituras de otros programas: el segundo inicializador espera y lee el Admin confirmado al ejecutar su SELECT posterior. La CLI no establece ninguna restricción global sobre la administración posterior, que puede gestionar otras cuentas según sus permisos. Referencias y procedimiento en AUTHENTICATION.md.

Motivo: resolver el bloqueo del primer administrador sin introducir una vía HTTP privilegiada ni contraseñas predeterminadas. Bloquear Users permite proteger la comprobación global incluso con la tabla vacía y frente a escritores que no usan un advisory lock común, sin migraciones ni estado persistente adicional.

Consecuencias: operación excepcional que requiere acceso PostgreSQL legítimo y permisos para SELECT/INSERT, secuencia y este modo de bloqueo; coordinarla porque el bloqueo afecta temporalmente a escrituras de Users. No mantener interacción humana ni el coste del hash bajo el bloqueo. No hay UPDATE/DELETE de usuarios existentes ni escrituras de Tickets. La condición es ausencia actual de Admin, no un registro permanente de que ya hubo un bootstrap. Pérdida de respuesta durante commit exige verificar el resultado; no hay recuperación de contraseña. No se garantiza borrado de strings de memoria ni protección frente a operadores con control del equipo/base. No hay nuevas dependencias NuGet, capas, modelos ni migraciones.

Pruebas: nueva referencia de Tests a la consola para verificar servicio/entrada y ejecutar el binario con una PTY Linux real (script/stty, sin grabaciones ni secretos en argumentos). Un caso sin PostgreSQL cancela por confirmación incorrecta y comprueba que no se muestra la contraseña. Integración añadida a la clase parcial existente: primera creación/login/JWT/Admin, rechazos, conservación de dos usuarios/seis tickets propios y dos procesos CLI realmente bloqueados a la vez. No se eliminan Admin ajenos para preparar casos: si existen en helpdesk_test_db, se falla con diagnóstico seguro. Se mantiene fixture/validador y limpieza por datos propios. Resultados realmente ejecutados en PROGRESS.md.


### 039 — Frontend Angular independiente y proxy de desarrollo (fase 10.1)

Estado: implementada el 27 de octubre de 2026. Angular 22.2 y TypeScript 6, con configuración estricta explícita, Router y componentes standalone. Proyecto hermano `HelpDesk.Web`, independiente de la solución y los proyectos .NET. Sin SSR ni bibliotecas visuales adicionales.

Decisión: organización por funcionalidad con `layout`, `features/home` y `core/api`; crear otras carpetas solo cuando haya código que las necesite. `HomeIntro` encapsula la presentación y sus estilos; `Home` gestiona el panel y `HealthService` ejecuta la petición HTTP. `provideHttpClient` se registra junto a `provideRouter`. La ruta inicial se carga bajo demanda y las desconocidas redirigen al inicio.

La única integración es GET relativo `/api/health`. Se valida el contrato real `{status: 'ok'}` antes de mostrar éxito, se limita la espera a ocho segundos y se cancela al destruir la página. Signals representan el estado visible; RxJS gestiona la petición. La comprobación no prueba disponibilidad de PostgreSQL ni autentica al usuario. No se consulta ni almacena JWT en esta fase.

Proxy registrado en `angular.json`: `/api/**` hacia `http://localhost:5085`, mientras Angular sirve en localhost:4200. Evita CORS en desarrollo sin modificar el backend. El proxy no forma parte de los archivos estáticos de producción; el despliegue deberá resolver `/api` mediante proxy inverso o una configuración CORS explícita si se usan orígenes separados. No hay secretos en la configuración pública del frontend.

Motivo: conectar incrementalmente la interfaz a la API probada, manteniendo responsabilidades simples y el backend intacto. El layout usa SCSS local y tokens globales, diseño responsive y elementos semánticos con accesibilidad básica.

Consecuencias: frontend y backend se arrancan por separado en desarrollo; `package-lock.json` permite instalaciones reproducibles. Vitest y HttpTestingController validan el cliente sin acceder a la base de datos. Login, guards por rol, interceptores JWT y gestión de tickets quedan para futuras tareas. Estrategia prevista de autenticación: token solo en memoria y autorización efectiva siempre en el servidor; aún no implementada. Resultados de validación en PROGRESS.md.


### 040 — Sesión JWT en memoria y autenticación Angular (fase 10.2)

Estado: implementada el 27 de octubre de 2026. Sin cambios de backend, política JWT, contratos, proxy ni dependencias.

Decisión: `AuthService` centraliza login, registro y sesión en signals. Solo conserva token, identidad y vencimiento en memoria; no usa almacenamiento del navegador ni cookies. La destrucción de la pestaña o la recarga elimina la sesión. El servicio resuelve HttpClient al necesitar una petición; observar el estado de sesión no crea peticiones ni requiere inicializar el cliente HTTP.

Login envía email/password a `/api/auth/login`. Antes de establecer sesión comprueba el contrato Bearer y la estructura de los claims sub/email/role/exp/nbf. Decodificarlos no verifica firma: sirven exclusivamente para presentación y navegación. La API valida firma, emisor, audiencia, vigencia y rol actual en cada acceso protegido. La caducidad del cliente usa el menor de exp y el plazo expiresIn, sin aprovechar la tolerancia posterior del servidor; nbf admite los 30 segundos de tolerancia del backend para pequeños desfases de reloj. Un temporizador y la comprobación antes de usar el token evitan conservar sesiones vencidas tras suspensión de la pestaña.

Registro envía únicamente name/email/password a `/api/auth/register`; la API decide User y devuelve un usuario público, no JWT. La UI vuelve al login sin iniciar sesión automáticamente. Los formularios usan validación reactiva; la longitud de contraseña se mide en unidades UTF-16 como .NET. Solo el correo se normaliza. El servidor mantiene la validación definitiva.

El interceptor funcional añade Bearer exclusivamente a URLs relativas `/api/users` y `/api/tickets`, tras normalizar segmentos y rechazar separadores codificados. Excluye URLs absolutas/externas y health/login/registro. Peticiones autenticadas y credenciales usan redirect:error para evitar reenvíos HTTP de secretos. No hay refresh tokens. HTTP 401 autenticado limpia y redirige; 403 y 409 se comunican de forma distinta sin borrar una sesión válida. Errores visibles son mensajes propios, nunca cuerpos o detalles del proveedor.

Cada sesión tiene una versión en memoria: un 401 de una petición antigua no invalida un login posterior; una respuesta de login anterior a logout o a otro login no restaura acceso. Las páginas cancelan peticiones al destruirse y limpian los controles de contraseña al finalizar. No se promete borrado inmediato de strings de memoria.

Rutas públicas login/registro y ruta privada session con guard funcional. La navegación cambia su etiqueta según User/Technician/Admin y conduce a la misma página de sesión; no hay rutas vacías de tickets o administración. Guard por roles preparado y probado para futuras páginas. Ocultar enlaces y bloquear rutas cliente no es una autorización de seguridad.

Consecuencias: recargar exige login y cada pestaña tiene su propia sesión. Logout es local; no revoca un JWT ya emitido porque el backend no ofrece esa función. Mantener el token en memoria reduce persistencia, pero no protege frente a scripts maliciosos/XSS: se conserva el escape de Angular y no se inserta HTML arbitrario. Fuera del desarrollo local, se requiere HTTPS. Registro manual válido solo contra una base aislada autorizada; los tests automatizados del cliente simulan HTTP y no escriben en PostgreSQL.
