# HelpDesk.Web

Frontend de HelpDesk.NET, fase 10.1. Angular 22, TypeScript estricto, componentes standalone, Angular Router y SCSS. Sin bibliotecas visuales adicionales ni SSR.

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
- `src/app/core/api`: consulta health tipada y validación de respuesta.
- `src/styles.scss`: tokens visuales y reglas globales; estilos de componentes en su propia carpeta.

La página consulta health al abrirse; el botón permite repetirla. Hay estados accesibles de carga, éxito y error. La petición expira a los ocho segundos y se cancela al destruir la página. No hay sondeo continuo ni acceso a datos de usuarios o tickets. Health solo confirma disponibilidad HTTP de la API.

## Validación

```bash
npm run build
npm test -- --watch=false
```

Las pruebas Vitest usan HTTP simulado: navegación, estados del panel, respuesta inválida, timeout, reintento y cancelación. No necesitan la API ni PostgreSQL.

No hay login, registro ni gestión de tickets en esta fase. Consultar el [README del repositorio](../README.md) y la [arquitectura](../docs/ARCHITECTURE.md) para el contexto completo.
