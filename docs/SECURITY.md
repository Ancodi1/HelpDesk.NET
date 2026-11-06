# Seguridad previa al despliegue — fase 10.7.2

Implementado el 6 de noviembre de 2026. Esta fase refuerza los hallazgos comunicados
para 10.7.1; no había un informe 10.7.1 versionado en este repositorio. No despliega
ni modifica datos habituales, JWT, roles, DTO de éxito o esquema PostgreSQL.

## Limitación de peticiones

`Security/RequestLimits.cs` configura el middleware integrado de ASP.NET Core.
Ventanas fijas, sin cola, con HTTP 429, JSON local seguro y `Retry-After` en segundos
redondeados hacia arriba. Se cuentan también intentos inválidos. Cada política tiene
su propio presupuesto; agotar login no consume registro ni lecturas.

| Operación POST | Cupo por ventana | Partición | Motivo |
| --- | --- | --- | --- |
| `/api/auth/login` | 10 / minuto | IP de conexión | Reducir intentos y coste de verificación de contraseñas |
| `/api/auth/register` | 5 / 10 minutos | IP de conexión | Reducir creación automatizada y coste de hashing |
| `/api/tickets` | 10 / minuto | ID de cuenta autorizada | Permitir uso humano y frenar creación masiva |
| `/api/tickets/{id}/comments` | 30 / minuto | ID de cuenta autorizada, compartido entre tickets | Permitir conversación y reducir spam |

Orden explícito: routing → autenticación → autorización → rate limiter → endpoint.
Los IDs proceden de CurrentAccount tras la autorización existente. Las peticiones
sin autorización a escrituras conservan 401/403 y no consumen el cupo de otra cuenta.
El middleware no cambia JWT ni la consulta de cuenta/rol vigente. Las lecturas, health,
PUT, PATCH y DELETE conservan su comportamiento sin estas cuotas.

Solo se utiliza `Connection.RemoteIpAddress` para rutas anónimas, normalizada a IPv6;
si no existe, se usa una partición común `unknown`. No se lee `X-Forwarded-For`,
`Forwarded` ni ninguna identidad del cuerpo. La fábrica de integración simula IP por
cliente mediante un middleware exclusivo de tests: ese encabezado no se interpreta
en producción. Las pruebas HTTP de cuotas usan los límites reales y acortan solo la
duración mediante DI para verificar recuperación.

### Condiciones que debe resolver el despliegue

- No se habilita Forwarded Headers ni se inventa una lista de proxies confiables.
  Con un proxy inverso todos sus clientes anónimos comparten la IP/cupo del proxy.
  Antes de publicar, definir la topología y configurar explícitamente KnownProxies /
  KnownIPNetworks y ForwardLimit en un cambio revisado, ejecutar ese middleware antes
  del limitador y restringir el acceso directo a la API. El proxy debe sustituir las
  cabeceras recibidas del cliente. No habilitar confianza indiscriminada ni
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED` como atajo.
- Usuarios tras NAT comparten cupos anónimos. No se bloquea una cuenta por correo:
  se evita que cualquiera bloquee a una víctima enviando su dirección.
- Contadores locales en memoria, independientes por proceso, reiniciados al arrancar.
  Varias instancias multiplican el cupo efectivo: imponer cuotas compartidas en gateway
  si se despliega así. No hay protección completa frente a ataques distribuidos.
- Ventanas fijas permiten ráfagas a ambos lados del cambio de ventana. Revisar métricas
  y carga en el destino antes de publicar; estos valores son una base para uso humano.
  El limitador de escrituras se ejecuta después de autorización, por lo que no limita
  el coste de autenticar/consultar cuentas. GET no tiene cuota en esta fase.

Referencia: [rate limiting de ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

## Entradas y persistencia

| Texto | Regla de aplicación (API y Angular) | PostgreSQL existente |
| --- | --- | --- |
| Título | Obligatorio, hasta 100 unidades UTF-16, sin NUL | varchar(100) |
| Descripción POST/PUT | Obligatoria, hasta 10.000 unidades UTF-16, sin NUL | text, sin nuevo check de longitud |
| Búsqueda de tickets | Opcional, hasta 200 unidades UTF-16, sin NUL; literal sin recortar | Parámetro ILIKE escapado, no se almacena |
| Nombre de usuario | Obligatorio, hasta 100 unidades UTF-16, sin NUL | varchar(100) y snapshots históricos de 100 |
| Correo | Hasta 254, formato existente y normalización, sin NUL | varchar(254), índice único existente |
| Contraseña de login | Obligatoria, máximo 128 unidades UTF-16, sin NUL; sin nuevo mínimo | No se almacena, solo verificación de hash |
| Contraseña de registro | Se conserva 15–128 UTF-16 y rechazo de controles | Hash existente |
| Comentario | Se conserva obligatorio, 4.000 UTF-16 y rechazo de NUL | varchar(4000) y checks existentes UTF-16/no blanco |

10.000 permite descripciones detalladas con tamaño acotado; 200 evita búsquedas
innecesariamente grandes; 128 coincide con el máximo del registro. No se recortan ni
truncan textos/contraseñas para hacerlos válidos. Los emojis cuentan dos unidades
UTF-16 en C# y JavaScript; PostgreSQL cuenta caracteres Unicode y no admite NUL.
Los límites UTF-16 son conservadores respecto a varchar y evitan errores previsibles
del proveedor. Descripción sigue siendo text: no se ha cambiado el esquema ni se han
revalidado/modificado filas existentes. SQL externo puede superar los límites de API.
Un check nuevo o una limpieza histórica requerirían autorización separada.

Entradas inválidas devuelven 400 con `error` local seguro, sin detalles PostgreSQL.
Login valida los tamaños antes de SQL/hash. Las escrituras conservan su autorización
y sus reglas 404/409 existentes; PUT de tickets comprueba existencia antes de validar
el texto. Angular valida formularios y búsquedas incluso desde URL, conserva sesiones
ante 429 y muestra un mensaje local. No hay reintentos automáticos de escrituras.

## Dependencias Angular

Auditoría inicial: `@angular/build` 22.2.4 → Vite 8.3.2, seis avisos Vite
(un aviso alto, cuatro moderados y uno bajo), propagados al builder. Se actualiza
`@angular/build` a 22.2.5 → Vite 8.3.5. Core/common/compiler/compiler-cli/forms/
platform-browser/router pasan de 22.2.3 a 22.2.5 para resolver coherentemente los pares
Angular; CLI permanece 22.2.4. TypeScript, RxJS y Vitest conservan sus versiones
instaladas. No se usa `--force`, `--legacy-peer-deps` ni overrides. Los intentos de
actualizar solo builder fallaron por resolución de pares; no se acepta ese árbol.

Los metadatos npm de builder 22.2.5 admiten Angular ^22, TypeScript >=6.0 <6.1 y
Vitest ^4.0.8 o ^5.0.0, compatibles con este proyecto. `npm ls --all` final correcto;
`npm audit` completo (incluye desarrollo) informa cero vulnerabilidades conocidas.
Esto no constituye garantía frente a avisos futuros. No publicar `ng serve`.

Fuentes: [aviso alto y versiones corregidas de Vite](https://github.com/advisories/GHSA-587h-7wmj-h5pm),
[versionado Angular](https://angular.dev/reference/releases), metadatos del registro npm
consultados para 22.2.5. Avisos iniciales adicionales: GHSA-698v-4r3x-3vcv,
GHSA-r825-w8mm-5pxw, GHSA-9jrq-w75r-8gcw, GHSA-rq7h-c2jc-7f22 y GHSA-vfpm-58rq-9qcg.

## Validación manual en un destino aislado

1. Desde la misma conexión, enviar once POST de login con JSON `{}` en menos de un
   minuto: diez 400 y después 429 con Retry-After. Cambiar X-Forwarded-For no evita
   el rechazo. Health sigue en 200. Tras esperar Retry-After, se admite otro intento.
2. Repetir registro con seis intentos: cinco 400 y sexto 429; recuperación tras diez
   minutos. Usar datos inválidos evita crear cuentas.
3. Con una cuenta de prueba autenticada, enviar once POST de tickets con `{}`:
   diez 400 y uno 429. Otra cuenta conserva su cupo; abrir listado/detalle sigue
   permitido. Para comentarios, usar JSON mal formado `{` y comprobar el intento 31;
   así se comprueba el límite sin publicar comentarios.
4. Crear/editar un ticket de prueba con 10.000 unidades UTF-16 y comprobar éxito;
   con 10.001 o `\u0000` comprobar 400 y conservación de los datos anteriores.
   Probar título/nombre con NUL, búsqueda de 201/NUL y login con 129/NUL.
   La búsqueda de 200 y el login con contraseña válida de 128 siguen admitidos.
5. En Angular, probar formularios, búsqueda por URL y mensajes 429, sin pérdida de
   sesión ni reenvío automático. Antes de publicar con proxy, repetir la matriz desde
   dos clientes y comprobar la IP efectiva usando exclusivamente proxies confiables.

Resultados automáticos y límites operativos en [PROGRESS.md](PROGRESS.md) y
[POSTGRESQL_TESTS.md](POSTGRESQL_TESTS.md). No ejecutar esta matriz contra datos reales.
