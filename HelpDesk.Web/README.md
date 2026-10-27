# HelpDesk.Web

Frontend de HelpDesk.NET, fases 10.1 a 10.3.2. Angular 22, TypeScript estricto, componentes standalone, Angular Router y SCSS. Sin bibliotecas visuales adicionales ni SSR.

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
- `src/app/features/tickets`: contrato, servicio HTTP, consulta URL, listado y tabla responsive; `create` y `detail` para creación y detalle.
- `src/app/core/api`: consulta health tipada y validación de respuesta.
- `src/styles.scss`: tokens visuales y reglas globales; estilos de componentes en su propia carpeta.

La página consulta health al abrirse; el botón permite repetirla. Hay estados accesibles de carga, éxito y error. La petición expira a los ocho segundos y se cancela al destruir la página. No hay sondeo continuo ni acceso a datos de usuarios o tickets. Health solo confirma disponibilidad HTTP de la API.

## Validación

```bash
npm run build
npm test -- --watch=false
```

Las pruebas Vitest usan HTTP simulado: navegación, estados del panel, respuesta inválida, timeout, reintento y cancelación. No necesitan la API ni PostgreSQL.

La fase 10.2 incluye `/login`, `/register` y `/session` con JWT solo en memoria, formularios reactivos, guard y cierre local de sesión. La recarga pierde el acceso; el backend conserva la autoridad de autorización. Las fases 10.3.1 y 10.3.2 incorporan `/tickets`, `/tickets/new` y `/tickets/:id`: listado, creación y detalle, sin edición, eliminación, asignación ni panel administrativo. El registro válido escribe una cuenta y el POST de creación escribe un ticket: comprobarlos solo contra una base aislada o un destino autorizado, según el README raíz. No se reintenta automáticamente una creación incierta. Las pruebas de autenticación y tickets usan HTTP simulado, nunca credenciales reales ni datos de desarrollo. El listado respeta los resultados autorizados por la API, sin filtrar permisos en el cliente. Consultar el [README del repositorio](../README.md) y la [arquitectura](../docs/ARCHITECTURE.md) para el contexto completo.
