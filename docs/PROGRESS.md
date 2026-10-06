# Progreso — HelpDesk.NET

Última actualización: 2026-10-06.

## Funcionalidades completadas

Todavía no hay funcionalidades de la aplicación implementadas.

Preparación completada:

- Contexto educativo y alcance del HelpDesk documentados.
- Reglas de colaboración y validación registradas en `AGENTS.md`.
- Registro inicial de decisiones arquitectónicas creado.

## Trabajo actual

Fase de preparación del proyecto completada. Pendiente de que se solicite la primera tarea de implementación.

No existe todavía una solución ni un proyecto ASP.NET Core. No se ha escrito código del HelpDesk.

## Problemas conocidos

- No se han identificado problemas de la aplicación porque aún no está implementada.
- Compilación y tests no son aplicables en esta fase documental. La disponibilidad del SDK de .NET 10 y del entorno de desarrollo está pendiente de comprobar antes de crear la aplicación.

## Próximos pasos

Estos pasos son orientativos y no autorizan su ejecución automática:

1. Acordar y solicitar la primera tarea: comprobar el entorno y crear una Web API mínima con .NET 10.
2. Aprender la estructura de ASP.NET Core, el arranque de la aplicación y el flujo de una petición HTTP.
3. Definir progresivamente el modelo de incidencias y sus reglas; incorporar persistencia con Entity Framework Core cuando corresponda.
4. Incorporar usuarios, técnicos, administradores, prioridades, categorías, asignaciones, estados, comentarios e historial mediante tareas concretas.
5. Añadir autenticación y autorización, tests automatizados, frontend con Angular y TypeScript y Docker en etapas posteriores solicitadas.

El orden podrá ajustarse según el aprendizaje y las necesidades reales. Cada cambio importante debe actualizar este documento y, si afecta al diseño, `ARCHITECTURE.md`.
