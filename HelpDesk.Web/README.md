# HelpDesk.Web

Frontend de HelpDesk.NET, fases 10.1, 10.2 y 10.3.1. Angular 22, TypeScript estricto, componentes standalone, Angular Router y SCSS. Sin bibliotecas visuales adicionales ni SSR.

## Desarrollo

Node 24.15.0 y npm 11.12.1 utilizados durante la implementación. Desde este directorio:

```bash
npm ci
npm start
```

Abrir http://localhost:4200. Iniciar también la API desde la raíz del repositorio en otra terminal con `dotnet run --project HelpDesk.Api --launch-profile http` y su configuración externa habitual. No se requieren migraciones para el frontend.

El proxy de `src/proxy.conf.json`, registrado en `angular.json`, dirige `/api/**` a http://localhost:5085. No contiene secretos y no se utiliza en el build desplegado. Reiniciar el servidor al cambiarlo.

## Organización

- `src/app/layout`: estructura compartida, navegación y pie de página.
- `src/app/features/home`: página de inicio y presentación del proyecto, cargadas bajo demanda.
- `src/app/features/auth`: formularios y sesión; `core/auth` conserva servicio, interceptor y guards.
- `src/app/features/tickets`: contrato, servicio GET, consulta URL, listado y tabla responsive.
- `src/app/core/api`: consulta health tipada y validación de respuesta.
- `src/styles.scss`: tokens visuales y reglas globales; estilos de componentes en su propia carpeta.

La página consulta health al abrirse; el botón permite repetirla. Hay estados accesibles de carga, éxito y error. La petición expira a los ocho segundos y se cancela al destruir la página. No hay sondeo continuo ni acceso a datos de usuarios o tickets. Health solo confirma disponibilidad HTTP de la API.

## Validación

```bash
npm run build
npm test -- --watch=false
```

Las pruebas Vitest usan HTTP simulado: navegación, estados del panel, respuesta inválida, timeout, reintento y cancelación. No necesitan la API ni PostgreSQL.

La fase 10.2 incluye `/login`, `/register` y `/session` con JWT solo en memoria, formularios reactivos, guard y cierre local de sesión. La recarga pierde el acceso; el backend conserva la autoridad de autorización. La fase 10.3.1 incorpora `/tickets`, de solo lectura, con búsqueda, filtros y paginación; sin creación, edición, eliminación ni panel administrativo. El registro válido escribe una cuenta: comprobarlo solo con una API apuntando a una base aislada, según el procedimiento del README raíz. Las pruebas de autenticación y tickets usan HTTP simulado, nunca credenciales reales ni datos de desarrollo. El listado respeta los resultados autorizados por la API, sin filtrar permisos en el cliente. Consultar el [README del repositorio](../README.md) y la [arquitectura](../docs/ARCHITECTURE.md) para el contexto completo.
