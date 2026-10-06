# Contexto y reglas de trabajo — HelpDesk.NET

## Objetivo

Proyecto educativo y de portfolio para aprender desarrollo profesional de forma incremental. La base prevista es .NET 10, ASP.NET Core Web API y Entity Framework Core. Más adelante se incorporarán Angular, TypeScript, autenticación, tests y Docker, cuando se soliciten y aporten valor al aprendizaje.

La aplicación prevista es un HelpDesk con usuarios, técnicos y administradores; gestión de incidencias, prioridades, categorías, asignaciones, estados, comentarios e historial. Este alcance describe la dirección del proyecto, no autoriza su implementación completa.

## Antes de implementar

- Lee este archivo, `docs/PROGRESS.md` y `docs/ARCHITECTURE.md`, además del código relevante existente.
- Antes de implementar cualquier funcionalidad, explica en español qué concepto de .NET estamos aprendiendo, qué archivos crearás o modificarás y por qué.
- Implementa únicamente las tareas solicitadas. Si falta una decisión que afecta al alcance, aclárala antes de implementar la parte dependiente.
- Divide el trabajo en cambios pequeños y comprensibles. Explica el código relevante, su propósito y cómo se relaciona con el concepto aprendido.

## Diseño y alcance

- No implementes Clean Architecture inicialmente. Comienza con una estructura sencilla y evoluciona solo ante necesidades concretas y autorizadas.
- No añadas tecnologías, dependencias, capas o patrones innecesarios ni funcionalidades futuras por iniciativa propia.
- No crees la aplicación ASP.NET Core ni código del HelpDesk hasta que se solicite expresamente. La fase inicial se limita a documentación.
- Nunca incluyas secretos ni credenciales en código, documentación, configuración versionada, ejemplos o salidas compartidas. Usa marcadores sin valores sensibles cuando sea necesario.

## Validación de cada cambio

- Compila y prueba después de cada cambio cuando exista una aplicación ejecutable. Ejecuta `dotnet build` y los tests disponibles con `dotnet test`; añade comprobaciones funcionales pertinentes al comportamiento modificado.
- Si aún no existe un proyecto .NET, verifica la documentación y el diff. Indica expresamente que compilación y tests no son aplicables; no crees una aplicación para cumplir esta validación.
- Si una comprobación no puede ejecutarse o falla, registra el motivo y comunica la limitación. No declares una validación como superada sin ejecutarla.
- Al terminar, resume qué cambió, explica el código relevante si lo hay y comunica las comprobaciones realizadas.

## Documentación viva

- Actualiza `docs/PROGRESS.md` cuando haya cambios importantes: funcionalidades completadas, trabajo actual, problemas conocidos y próximos pasos. Distingue lo implementado de lo previsto.
- Actualiza `docs/ARCHITECTURE.md` cuando haya cambios importantes de arquitectura: decisión, estado, motivo y consecuencias. No presentes decisiones pendientes como definitivas.
- Mantén ambos archivos alineados con el estado real del repositorio.
