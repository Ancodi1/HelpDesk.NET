# Arquitectura — HelpDesk.NET

Última actualización: 2026-10-06.

## Estado actual

El repositorio está en fase documental. No existe aplicación, solución .NET, modelo de datos ni infraestructura desplegada. Las decisiones siguientes orientan la implementación futura y no implican que esté realizada.

## Decisiones iniciales

### 001 — Desarrollo incremental orientado al aprendizaje

Estado: aceptada.

Decisión: implementar únicamente tareas solicitadas, en cambios pequeños, explicando antes el concepto .NET y los archivos afectados.

Motivo: comprender las decisiones y el código, y construir un portfolio que refleje aprendizaje profesional.

Consecuencia: el alcance funcional completo se desarrollará por etapas; cada etapa se validará y documentará.

### 002 — Backend previsto con .NET 10, ASP.NET Core Web API y EF Core

Estado: aceptada como dirección tecnológica; implementación pendiente.

Decisión: utilizar .NET 10 y ASP.NET Core Web API para el backend, e incorporar Entity Framework Core para persistencia en la etapa correspondiente.

Motivo: aprender desarrollo de APIs y acceso a datos dentro del ecosistema .NET.

Consecuencia: antes de implementar se comprobará el entorno. La elección de base de datos y proveedor de EF Core permanece pendiente; no se añaden dependencias todavía.

### 003 — Estructura inicial sencilla

Estado: aceptada.

Decisión: no adoptar Clean Architecture inicialmente ni añadir capas o patrones sin una necesidad concreta. La estructura de archivos se decidirá al solicitar la creación de la API.

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

## Mantenimiento del registro

Ante un cambio arquitectónico importante, registra la decisión, su estado, motivos y consecuencias. Si reemplaza una decisión previa, conserva el motivo de la evolución y actualiza el estado anterior. Refleja también el avance en `PROGRESS.md`.
