# Arquitectura — HelpDesk.NET

Última actualización: 2026-10-20.

## Estado actual

El repositorio contiene la solución `HelpDesk.NET.slnx` y los proyectos `HelpDesk.Api` (ASP.NET Core Web API) y `HelpDesk.Tests` (pruebas unitarias xUnit), ambos para `net10.0`. `HelpDeskDbContext` está registrado mediante DI con Npgsql y `ConnectionStrings:DefaultConnection`. El cierre del 8 de octubre de 2026 confirmó mediante consultas de lectura PostgreSQL 16.15, la base `helpdesk_db` y `InitialCreate` aplicada en `__EFMigrationsHistory`. Todo el CRUD accede a `Tickets`; la comprobación del 13 de octubre confirmó creación y lectura de un ticket incluso tras reiniciar la API.

`GET /api/tickets` inyecta el contexto y usa `ToListAsync` para devolver HTTP 200 con el array de tickets, incluido `[]` si la tabla está vacía. `GET /api/tickets/{id}` usa `FirstOrDefaultAsync` y devuelve 200 con el objeto o 404 sin cuerpo. Health mantiene HTTP 200 con `{"status":"ok"}`.

POST inyecta el contexto, registra el nuevo ticket con `Add` y lo persiste con `SaveChangesAsync` antes de responder 201. PUT consulta una entidad seguida por EF Core con `FirstOrDefaultAsync`, valida y guarda sus textos con `SaveChangesAsync` antes de responder 200. DELETE consulta por ID con `FirstOrDefaultAsync`, elimina mediante `Remove` y persiste con `SaveChangesAsync`; devuelve 404 o 204. La lista temporal ya no existe. No hay servicios, repositorios ni capas adicionales.

La serialización JSON de las Minimal APIs se configura centralmente con `ConfigureHttpJsonOptions` y `JsonStringEnumConverter`: los estados se devuelven como texto (decisión 019). Las referencias a JSON numérico en decisiones anteriores son históricas.

PATCH aplica las transiciones definidas en `TicketStatusTransitions` y devuelve 409 para cambios prohibidos; repetir el estado actual devuelve 200 sin guardar (decisión 021).

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

## Mantenimiento del registro

Ante un cambio arquitectónico importante, registra la decisión, su estado, motivos y consecuencias. Si reemplaza una decisión previa, conserva el motivo de la evolución y actualiza el estado anterior. Refleja también el avance en `PROGRESS.md`.
