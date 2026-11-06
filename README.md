# HelpDesk.NET

Aplicación de gestión de incidencias técnicas: permite registrar tickets, actualizar su información y clasificarlos por estado, prioridad y categoría.

Es un **proyecto personal en desarrollo**, con un enfoque educativo y de portfolio. Su objetivo es practicar el diseño de APIs, la persistencia relacional y las pruebas automatizadas mediante una evolución incremental. Actualmente ofrece una API HTTP con autenticación JWT y autorización por roles, y una primera interfaz Angular responsive con comprobación de disponibilidad de la API. El frontend incorpora login, registro y sesión solo en memoria. El frontend incorpora listado, creación y detalle de incidencias. El detalle permite editar y gestionar tickets según los permisos del backend; la eliminación desde Angular queda fuera de alcance.

[Repositorio en GitHub](https://github.com/Ancodi1/HelpDeskt.NET)

## Demo segura de portfolio — fase 10.7.3

Modo demo opcional, desactivado por defecto: cuentas públicas User/Technician y
Admin privado, identidades protegidas en backend, registro cerrado y datos ficticios.
La consola `HelpDesk.Demo` inicializa/restaura solo una base marcada explícitamente,
sin borrar cuentas ni ejecutar migraciones. La interfaz identifica la demo y permite
elegir una cuenta pública; sus credenciales se configuran fuera de Git.

Límites globales en demo: 100 tickets, 500 comentarios y 1.000 actividades, además de
las cuotas 10.7.2. Contenido compartido entre visitas y restauración operada de forma
privada. El modo normal conserva sus permisos y funcionamiento.

[Configuración, dataset, restauración y validación manual](docs/DEMO.md).
Implementado y probado en Docker aislado; aún sin aprovisionamiento público ni despliegue.

## Refuerzo de seguridad — fase 10.7.2

Login: 10 intentos/minuto por IP de conexión; registro: 5/10 minutos por IP.
Creación de tickets: 10/minuto por cuenta; comentarios: 30/minuto por cuenta.
Ventanas fijas, sin cola, HTTP 429 con Retry-After; lecturas sin cuota.
Descripciones hasta 10.000 unidades UTF-16, búsquedas hasta 200 y login hasta 128;
rechazo de NUL en textos de entrada. Builder Angular 22.2.5 con Vite 8.3.5.
No hay cambios de esquema, JWT o roles ni despliegue.

Consulta [seguridad, límites, proxy inverso y validación manual](docs/SECURITY.md).
Los contadores son locales por instancia; tras un proxy las rutas anónimas comparten
su IP hasta configurar explícitamente proxies confiables. La publicación queda pendiente.

## Tecnologías

| Tecnología | Uso |
| --- | --- |
| C# y .NET 10 | Lenguaje y plataforma del backend |
| ASP.NET Core Minimal APIs | Endpoints HTTP y serialización JSON |
| Entity Framework Core 10 | Acceso a datos y migraciones |
| PostgreSQL y Npgsql | Persistencia relacional |
| Angular 22, TypeScript 6 y SCSS | Frontend standalone, tipado estricto y diseño responsive |
| Vitest | Pruebas del frontend con HTTP simulado |
| xUnit | Pruebas unitarias y de integración |
| WebApplicationFactory / TestServer | Pruebas HTTP en memoria |

## Frontend — fases 10.1 a 10.6.2.2

`HelpDesk.Web` es un proyecto Angular independiente de la solución .NET. Incluye navegación básica, página de inicio responsive y un panel que consulta `GET /api/health`, con estados de carga, conexión correcta y error y un botón para volver a comprobar. Incluye formularios reactivos de login/registro y una sesión JWT solo en memoria, listado con filtros y paginación, creación y detalle de tickets. Incluye gestión en el detalle, panel de técnicos y panel administrativo con directorio, edición de usuarios y selección de responsables. No incluye eliminación desde Angular.

Requisitos: Node.js compatible con Angular 22 (desarrollo validado con Node 24.15.0 y npm 11.12.1). No hace falta instalar Angular CLI globalmente: los scripts utilizan la versión local. `package-lock.json` está versionado; en una copia nueva, ejecutar:

```bash
cd HelpDesk.Web
npm ci
```

Desde la raíz del repositorio, arrancar el backend en una terminal con su configuración externa de desarrollo ya existente:

```bash
dotnet run --project HelpDesk.Api --launch-profile http
```

En otra terminal, desde la raíz:

```bash
cd HelpDesk.Web
npm start
```

Abrir **http://localhost:4200/**. El panel debería mostrar «La API está disponible». La primera petición se realiza automáticamente y puede repetirse con el botón. Si la API no responde, aparece un error recuperable tras un máximo de ocho segundos. La comprobación health no consulta PostgreSQL ni demuestra la salud de la base de datos.

Angular llama a `/api/health`; `src/proxy.conf.json` reenvía `/api/**` a `http://localhost:5085` conservando la ruta. El proxy se configura en `angular.json`, por lo que `npm start` lo activa automáticamente. No se han añadido CORS ni cambios al backend. En desarrollo, el navegador utiliza el mismo origen de Angular; el proxy es exclusivo del servidor de desarrollo. Un despliegue futuro necesitará configurar su propio proxy inverso o CORS con orígenes explícitos. Reiniciar `npm start` si se cambia el archivo del proxy.

Comprobaciones desde `HelpDesk.Web`:

```bash
npm run build
npm test -- --watch=false
```

Las pruebas Angular utilizan HTTP simulado; no acceden a PostgreSQL ni a la API real. Para comprobar el proxy con ambos servidores arrancados:

```bash
curl --fail --silent --show-error http://localhost:4200/api/health
```

La respuesta esperada es `{"status":"ok"}`. Los servidores se detienen con Ctrl+C en sus respectivas terminales. La estructura y decisiones se describen en [ARCHITECTURE.md](docs/ARCHITECTURE.md); el estado y las validaciones, en [PROGRESS.md](docs/PROGRESS.md).

### Autenticación Angular — fase 10.2

Rutas públicas `/login` y `/register`; ruta privada `/session`. Los formularios usan los contratos existentes: login envía `email` y `password` y recibe `accessToken`, `tokenType` y `expiresIn`; registro envía `name`, `email` y `password` y recibe el usuario público con HTTP 201. El rol lo fija la API en User: no se envía ningún campo de rol. Tras registrar, se vuelve al login sin autenticar automáticamente.

El correo se normaliza; la contraseña nunca se recorta ni transforma. Registro exige nombre no vacío de hasta 100 unidades UTF-16, correo con dominio completo de hasta 254 y contraseña de 15–128 unidades UTF-16, sin controles ni solo espacios. Login exige correo y contraseña no vacíos, contraseña de hasta 128 unidades UTF-16 y ausencia de NUL; no aplica el mínimo del registro. La validación del servidor sigue siendo la autoridad final.

El JWT, el usuario y la caducidad solo viven en memoria. Recargar o cerrar la pestaña pierde la sesión; cada pestaña mantiene su propia sesión. No hay localStorage, sessionStorage, cookies JWT, refresh tokens ni claves de firma en el cliente. El cierre de sesión es local y no revoca copias del JWT: el backend actual no ofrece revocación.

El interceptor funcional adjunta Bearer únicamente a las rutas relativas protegidas de `/api/users` y `/api/tickets`; nunca a health, login, registro, URLs absolutas o dominios externos. Las peticiones autenticadas y las de credenciales rechazan redirecciones HTTP. La sesión vence en el primero de los límites `exp` y `expiresIn`; un 401 autenticado la elimina y vuelve al login. 403 indica falta de permiso, 409 conflicto y los errores de red/timeout muestran mensajes seguros, sin reflejar la respuesta del proveedor. Los guards y los claims solo orientan la navegación; .NET valida la firma y autoriza cada petición protegida.

#### Comprobación manual sin cambiar desarrollo

1. Arrancar backend y frontend con los comandos anteriores. Abrir `/session` sin iniciar sesión: debe redirigir a `/login`.
2. En `/login`, introducir en el navegador una cuenta existente con su contraseña conocida. No copiarlas al código, al chat ni a registros. Tras HTTP 200 debe abrirse `/session`, mostrarse correo/rol y aparecer «Cerrar sesión». La navegación se adapta a User, Technician y Admin, pero todavía no enlaza a paneles o tickets.
3. Pulsar «Cerrar sesión»: vuelve al login y desaparece la identidad. `/session` debe quedar bloqueada. Iniciar sesión y recargar también debe perder la sesión. La caducidad normal es de 30 minutos; las pruebas automatizadas comprueban la expiración sin esperar ese tiempo.
4. Abrir `/register` y enviar campos vacíos o una contraseña demasiado corta: deben aparecer errores locales sin enviar una petición de registro.
5. **Un registro válido crea una cuenta. Para probarlo sin escribir en `helpdesk_db`, usar exclusivamente una instancia de la API configurada y revisada para una base aislada ya preparada, como `helpdesk_test_db`.** No ejecutar un registro válido contra desarrollo durante esta verificación. El backend de pruebas debe ocupar el puerto 5085, sin mantener otra API en ese puerto.

Para arrancar esa API aislada sin guardar su conexión en archivos o argumentos, desde la raíz y tras comprobar manualmente el destino y su usuario:

```bash
(
  read -r -s -p 'Conexión exclusiva de helpdesk_test_db revisada: ' ConnectionStrings__DefaultConnection
  printf '\n'
  export ConnectionStrings__DefaultConnection
  dotnet run --project HelpDesk.Api --launch-profile http
)
```

La API requiere también su configuración JWT externa habitual. Este comando no valida por sí solo el destino introducido: debe corresponder a la base aislada revisada. No ejecuta migraciones ni crea la base. Al terminar el proceso, el subshell descarta la variable.

Con ese destino aislado, enviar `/register` con nombre, correo disponible y contraseña válida: se espera HTTP 201 y regreso al login con confirmación. Iniciar sesión con la nueva cuenta debe mostrar User. Repetir el registro del mismo correo debe mostrar el conflicto 409. No se ha realizado ninguna de estas escrituras durante la implementación. No imprimir ni compartir el JWT de las herramientas del navegador.

### Listado de incidencias — fase 10.3.1

La ruta protegida **`/tickets`** consulta exclusivamente `GET /api/tickets` a través del proxy y del interceptor JWT existente. No escribe datos. La API determina tanto las filas como sus totales: User ve las creadas por él; Technician, las creadas por él o asignadas a él; Admin, todas. El frontend no solicita filas de otros usuarios ni intenta reconstruir permisos mediante filtros locales.

| Parámetro | Contrato |
|---|---|
| `search` | Búsqueda literal en título o descripción, sin distinguir mayúsculas. Se conservan espacios y caracteres como `%` o `_`. Debounce de 350 ms. |
| `status` | `Open`, `InProgress`, `Resolved`, `Closed` |
| `priority` | `Low`, `Medium`, `High`, `Critical` |
| `category` | `Hardware`, `Software`, `Network`, `Access`, `Other` |
| `page` | Entero positivo, por defecto 1; desplazamiento máximo compatible con la API. |
| `pageSize` | De 1 a 100, por defecto 10. El selector ofrece tamaños habituales y respeta otros tamaños válidos recibidos en la URL. |

El orden es fijo: `CreatedAt` descendente e ID descendente para desempatar. **No existe un parámetro de ordenación configurable.** La respuesta real es `{ items, totalCount, page, pageSize, totalPages }`. Los tickets incluyen id, título, descripción, fecha UTC, estado/prioridad/categoría como strings y IDs de creador/técnico que pueden ser null. Las relaciones completas de usuarios no se serializan. Por eso la tabla muestra «Sin asignar» o el ID del técnico, sin inventar nombres ni hacer consultas adicionales a usuarios.

La vista usa tabla en escritorio y tarjetas en móvil, con etiquetas de estado/prioridad. Las fechas se presentan en hora local. Incluye carga, error seguro, reintento, ausencia de incidencias, ausencia de coincidencias y recuperación a primera página si la página solicitada queda fuera del rango. El listado conserva sus filtros al abrir el detalle desde el título o el formulario desde «Nuevo ticket». No incluye edición, eliminación o asignación.

Filtros, búsqueda, tamaño y página se conservan en query parameters. Cambiar filtros o tamaño vuelve a página 1; buscar reemplaza la entrada del historial para no crear una por cada escritura. Volver atrás restaura controles y consulta. Parámetros repetidos, filtros desconocidos, números inválidos y parámetros no soportados se normalizan en la URL antes de consultar. Cualquier cambio cancela la petición anterior y también una búsqueda pendiente; abandonar la página cancela ambas. Las respuestas no se guardan en almacenamiento local ni se mezclan con datos simulados.

Para probar manualmente, con API y frontend arrancados según los comandos anteriores:

1. Abrir `/tickets` sin sesión: debe redirigir a `/login` antes de pedir tickets.
2. Iniciar sesión con una cuenta existente y pulsar «Tickets» en la barra lateral, o abrir `http://localhost:4200/tickets`. El enlace se identifica como activo también cuando hay filtros en la URL. No crear ni modificar cuentas para preparar esta prueba.
3. Revisar títulos, estados, prioridades, categorías, fechas y asignación. Una cuenta sin tickets visibles puede mostrar legítimamente una lista vacía.
4. Escribir una búsqueda, combinar filtros y cambiar el tamaño de página. Comprobar que la URL refleja la consulta y que los cambios de filtro vuelven a la primera página.
5. Usar Anterior/Siguiente cuando haya varias páginas; si no hay suficientes filas, no añadir tickets para forzar el caso: la paginación también está cubierta por pruebas aisladas.
6. Volver atrás en el navegador y comprobar los filtros restaurados. Probar una página fuera de rango, por ejemplo `?page=999999`, y volver a la primera página con el botón disponible si existen resultados.
7. Si se dispone de cuentas existentes activas para otros roles, repetir y comparar únicamente con la visibilidad que concede la API. No cambiar roles ni contraseñas. El 401 continúa limpiando la sesión mediante el interceptor; 403 se presenta como falta de permiso.

La sesión sigue siendo solo en memoria: recargar pierde el acceso. Tras volver a iniciar sesión se puede abrir nuevamente la URL del listado con sus filtros. Las pruebas unitarias usan respuestas HTTP aisladas; no se ejecuta una autenticación real ni se consulta PostgreSQL durante su ejecución.

### Creación y detalle de tickets — fase 10.3.2

Rutas privadas `/tickets/new` y `/tickets/:id`, con el guard y el interceptor JWT existentes. `/tickets/new` se declara antes de la ruta de ID para evitar interpretar «new» como identificador. User, Technician y Admin pueden crear; el servidor valida los permisos y fija el creador usando la cuenta autenticada.

El formulario envía exclusivamente `title`, `description`, `priority` y `category` a `POST /api/tickets`. Título obligatorio, no solo espacios y máximo 100 unidades UTF-16; descripción obligatoria y no solo espacios, sin añadir un límite que no exista en la API. Prioridad inicial Medium y categoría Other, con los enums reales. No se recorta ni transforma el texto. El DTO del servidor contiene también CreatedByUserId, pero lo ignora: por eso no se muestra ni se envía. Tampoco se envían estado, fecha, ID o asignación.

Mientras el POST está pendiente se desactivan los campos y el envío. HTTP 201 con un Ticket válido confirma la creación y redirige a su detalle usando el ID del cuerpo; nunca se navega a una URL recibida en Location. La confirmación incluye un enlace al detalle si falla la navegación y el formulario no permite repetir un envío ya confirmado. El aviso de creación se transmite al detalle como estado de navegación efímero, sin almacenar tokens ni el contenido del ticket.

El detalle consulta únicamente GET /api/tickets/{id}. Muestra título, descripción completa con saltos de línea, estado, prioridad, categoría, fecha de creación e IDs de creador/técnico, admitiendo null. No inventa nombres ni fechas de modificación. Usa las mismas etiquetas y colores que el listado. El enlace de vuelta conserva los filtros y la página; abrir el formulario también conserva ese contexto, sin convertir los filtros en valores iniciales del nuevo ticket.

La API sigue decidiendo la visibilidad: User ve sus incidencias; Technician, propias o asignadas; Admin, todas. HTTP 404 significa inexistente **o no visible**, y se muestra el mismo mensaje. El cliente no intenta distinguirlos ni pide información adicional de usuarios. Hay carga, error, reintento de lectura y cancelación de peticiones obsoletas o al salir. Los mensajes 400/401/403/404/409/red son propios y no reflejan cuerpos del proveedor. El 401 sigue siendo responsabilidad del interceptor actual.

**Límite del backend:** no hay idempotencia de creación. Una desconexión, timeout o cancelación del cliente no demuestra que el servidor haya revertido el POST. No hay reintentos automáticos; ante un resultado incierto se indica revisar el listado antes de enviar otra vez. No se ha cambiado el backend para resolver este límite.

#### Prueba manual de creación y detalle

Los tests automatizados de esta fase usan HTTP simulado y no crean filas reales. **Una prueba manual válida de creación sí escribe un ticket**: si se desea conservar `helpdesk_db` intacta, utilizar una API conectada a una base aislada ya preparada y una cuenta existente con credenciales válidas. La configuración externa y el arranque aislado se explican en la sección de autenticación anterior. No ejecutar migraciones ni crear usuarios para preparar esta comprobación.

1. Desde la raíz, arrancar la API con la configuración del destino autorizado: `dotnet run --project HelpDesk.Api --launch-profile http`.
2. En otra terminal, desde `HelpDesk.Web`, ejecutar `npm start` y abrir http://localhost:4200/login.
3. Iniciar sesión con una cuenta existente y abrir Tickets. Pulsar **Nuevo ticket**: debe abrir `/tickets/new` para cualquiera de los tres roles autenticados.
4. Comprobar primero los errores locales dejando título/descripción vacíos o escribiendo un título de más de 100 unidades UTF-16: no debe enviarse el POST.
5. Solo en el destino donde se haya autorizado escribir, introducir título y descripción no sensibles, elegir prioridad/categoría y pulsar **Crear ticket**. El formulario se bloquea durante el envío. Tras 201 debe abrirse `/tickets/<id>` y mostrarse la confirmación junto a los datos reales.
6. Volver al listado y pulsar el título del ticket para abrir su detalle. Comprobar que los filtros y la página se conservan al volver; el nuevo ticket puede no aparecer si no cumple los filtros activos.
7. Para errores de lectura, consultar un ID comprobado como inexistente, sin crear datos para preparar el caso. Debe mostrarse el mensaje de incidencia no disponible. Sin sesión, tanto creación como detalle deben llevar al login sin consultar tickets.
8. No reenviar una creación que haya terminado en timeout o desconexión sin revisar primero si ya existe. No usar edición, eliminación ni asignación para preparar o limpiar esta prueba: siguen fuera del alcance del frontend.

Las comprobaciones con una cuenta real quedan para la ejecución manual del usuario. La recarga sigue perdiendo la sesión en memoria y exige volver a iniciar sesión antes de abrir el detalle.

### Panel de técnicos — fase 10.4

`/my-tickets` reutiliza el listado y está disponible únicamente para `Technician`, mediante el enlace **Mis incidencias**. Consulta `assignedToMe=true`: muestra solo asignaciones a la cuenta autenticada, no todas las incidencias creadas por ella. Conserva búsqueda, estado, prioridad, categoría, tamaño y página en la URL. El listado general `/tickets` mantiene su comportamiento para los tres roles.

Los contadores de abiertas, en progreso y resueltas usan `totalCount` de tres consultas independientes con `pageSize=1`; no incluyen cerradas ni dependen de los filtros del listado. No representan una instantánea transaccional conjunta. Cambiar filtros no recarga el resumen; volver desde el detalle sí lo consulta de nuevo. El resumen tiene su propio reintento de lectura.

El detalle conserva el origen mediante `from=my-tickets` y permite regresar al panel con filtros/página. Se reutiliza la edición de estado existente para el técnico asignado; la autorización definitiva sigue en la API. HTTP 401 termina la sesión mediante el interceptor; 403 comunica falta de permiso sin cerrar sesión ni reintentar automáticamente.

#### Comprobación manual del panel

1. Arrancar la API con configuración externa del destino autorizado (`dotnet run --project HelpDesk.Api --launch-profile http`) y Angular desde `HelpDesk.Web` (`npm start`). No ejecutar migraciones ni crear datos para preparar esta revisión.
2. Iniciar sesión con una cuenta Technician existente y abrir **Mis incidencias**. En la pestaña de red comprobar una consulta de listado y tres de resumen, todas con `assignedToMe=true`; los contadores usan estados Open/InProgress/Resolved y `pageSize=1`.
3. Usar filtros y paginación sobre los datos disponibles. Los contadores deben permanecer independientes. Abrir un ticket y volver mediante **Volver al listado**: deben conservarse filtros y página. Si no existen asignaciones, comprobar el estado vacío sin crear ninguna.
4. Con User o Admin, comprobar que no aparece el enlace y que `/my-tickets` redirige a `/session`. Sin sesión debe redirigir a `/login`. Confirmar que User sigue accediendo a Tickets y a Nuevo ticket.
5. La comprobación anterior es de lectura. Solo en un entorno aislado con autorización explícita para escribir, cambiar el estado de un ticket ya asignado al técnico, volver y comprobar la actualización de los contadores. No modificar tickets reales para esta prueba. Los casos 401/403 y cambios de estado también están cubiertos con HTTP simulado.

Validación automatizada del cierre: 265 pruebas .NET sin PostgreSQL y 270 Angular superadas, cero fallidas; ambos builds correctos. Vitest ejecuta los archivos en serie mediante `vitest.config.mjs` para evitar contención entre suites Angular/jsdom, manteniendo aislamiento, aserciones y timeouts predeterminados. No se ejecutaron pruebas PostgreSQL ni la revisión visual autenticada; detalles en [PROGRESS.md](docs/PROGRESS.md).

## Funcionalidades

- CRUD de tickets con persistencia en PostgreSQL.
- Consulta de usuarios y registro público de cuentas con contraseña y rol `User`.
- Autorización por roles y recursos: tickets propios/asignados y administración exclusiva de Admin.
- CLI independiente para crear el primer Admin con confirmación del destino, contraseña oculta y bloqueo PostgreSQL; creación real y acceso administrativo verificados.
- Creador opcional y asignación/reasignación/desasignación de técnicos en tickets.
- Listado con filtros combinables, búsqueda sin distinguir mayúsculas y paginación.
- Validación de título obligatorio, máximo de 100 caracteres, y descripción obligatoria.
- Estados con transiciones controladas y respuesta `409 Conflict` cuando el cambio no está permitido.
- Prioridades: `Low`, `Medium`, `High` y `Critical`.
- Categorías: `Hardware`, `Software`, `Network`, `Access` y `Other`.
- Actualizaciones independientes de estado, prioridad y categoría. Repetir el valor actual devuelve `200` sin una escritura innecesaria.
- Enums como texto en PostgreSQL y JSON; fechas de creación en UTC.
- Errores de validación en JSON y respuestas breves para cuerpos JSON incorrectos.

| Estado actual | Estados de destino permitidos |
| --- | --- |
| `Open` | `InProgress` |
| `InProgress` | `Resolved`, `Open` |
| `Resolved` | `Closed`, `InProgress`, `Open` |
| `Closed` | `Open` |

## Estructura

```text
HelpDesk.NET.slnx
HelpDesk.Api/
  Program.cs          # Arranque, configuración y endpoints
  Models/             # Ticket, enums y reglas de negocio
  DTOs/               # Contratos de entrada y respuesta
  Validation/         # Validación compartida de solicitudes de usuarios
  Data/               # DbContext y mapeo de entidades
  Migrations/         # Evolución del esquema PostgreSQL
HelpDesk.AdminBootstrap/ # CLI manual del primer Admin; no arranca con la API
HelpDesk.Demo/           # Consola privada de datos demo; destino verificado, sin migraciones
HelpDesk.Tests/
  Integration/        # Fábrica HTTP, fixture y pruebas PostgreSQL
  ...                 # Pruebas unitarias y del endpoint de salud
  HelpDesk.Tests.csproj
docs/                 # Arquitectura, progreso y guías de configuración
dotnet-tools.json     # Herramienta local dotnet-ef
```

La solución mantiene una estructura sencilla: los endpoints utilizan directamente el contexto de EF Core y las reglas de transición se expresan en una función independiente.

## Usuarios y roles

`User` conserva Id, Name (máximo 100), Email (máximo 254), Role y CreatedAt UTC. Añade `PasswordHash` nullable: los usuarios anteriores conservan null y sus relaciones con tickets. No se generan contraseñas ficticias. Los DTOs públicos solo contienen `id`, `name`, `email`, `role`, `createdAt`; el hash lleva además `JsonIgnore` en la entidad.

`POST /api/auth/register` recibe `name`, `email` y `password`. Reutiliza la validación de nombre/correo, normaliza Email con `Trim().ToLowerInvariant()` y crea exclusivamente `User`; cualquier campo adicional `role`, `passwordHash`, `id` o `createdAt` se ignora. Devuelve 201 con DTO público y Location `/api/users/{id}`, 400 por entrada inválida o 409 por correo duplicado. El índice único protege también registros concurrentes. No reemplaza contraseñas de cuentas anteriores.

La contraseña exige 15–128 unidades UTF-16, no puede ser solo espacios ni contener caracteres de control. Permite Unicode y espacios; no se recorta, normaliza ni trunca. Se usa `PasswordHasher<User>` V3 (PBKDF2-HMAC-SHA512, 210.000 iteraciones y salt aleatorio), sin instalar Identity completo. Hay login con JWT firmado HS256 de 30 minutos; hay autorización por roles y recursos, sin recuperación ni envío de correos. Ver [configuración y prueba local de autenticación](docs/AUTHENTICATION.md).

Email conserva la política anterior: formato mediante `MailAddress.TryCreate`, sin nombres de presentación ni espacios internos, dominio ASCII con al menos un punto; dominios internacionalizados en punycode. PostgreSQL exige correo canónico mediante CHECK e índice único. Los GET proyectan DTOs con AsNoTracking y el listado sigue siendo un array ordenado por Id.

**Etapa 9.3:** health, registro y login son públicos; desde 10.7.3 también lo es GET /api/demo, que en modo normal solo informa enabled=false. Toda otra ruta requiere JWT válido y una cuenta existente con el mismo rol actual en PostgreSQL. POST/PUT de usuarios se habilitan solo para Admin; no permiten fijar contraseñas/hashes. Las cuentas administrativas creadas así tienen PasswordHash NULL y requieren un futuro flujo seguro de activación. La consola separada `HelpDesk.AdminBootstrap` permite crear el primer Admin nuevo solo si no existe ninguno, con destino y confirmación interactivos. No activa cuentas antiguas ni modifica las existentes. El primer Admin real ya fue creado y su acceso administrativo verificado. Perfil propio disponible en GET `/api/users/{id}`; directorio completo reservado a Admin.

## API

URL del perfil HTTP local: `http://localhost:5085`. Salvo health, registro y login, enviar `Authorization: Bearer <TOKEN_LOCAL>`; los ejemplos de gestión requieren Admin. No ejecutar escrituras de ejemplo sobre datos existentes.

| Método | Ruta | Comportamiento |
| --- | --- | --- |
| GET | `/api/users` | Solo Admin; `200` con DTOs ordenados por Id |
| GET | `/api/users/{id}` | Admin o perfil propio; `200` con DTO o `404` |
| POST | `/api/auth/login` | `200` con accessToken, tokenType Bearer y expiresIn 1800; `400` o `401` genérico |
| POST | `/api/auth/register` | `201` con DTO y Location, `400` o `409`; siempre rol User |
| POST | `/api/users` | Solo Admin; `201`, `400` o `409`; sin credencial |
| PUT | `/api/users/{id}` | Solo Admin; `200`, `400`, `404` o `409` |
| GET | `/api/health` | `200` con `{"status":"ok"}` |
| GET | `/api/tickets` | `200` con `items` y metadatos de paginación |
| GET | `/api/tickets/{id}` | `200` con el ticket, o `404` |
| GET | `/api/tickets/{id}/comments` | Usuario autorizado a ver el ticket; paginación por cursor |
| POST | `/api/tickets/{id}/comments` | Usuario autorizado a ver el ticket; `201`, `400`, `401`, `403`, `404` o `409` |
| POST | `/api/tickets` | `201` con ticket y Location, `400` o `409` por conflicto de referencia concurrente |
| PUT | `/api/tickets/{id}` | Actualiza título y descripción; `200`, `400` o `404` |
| PATCH | `/api/tickets/{id}/status` | Cambia el estado; `200`, `400`, `404` o `409` |
| PATCH | `/api/tickets/{id}/priority` | Cambia la prioridad; `200`, `400` o `404` |
| PATCH | `/api/tickets/{id}/category` | Cambia la categoría; `200`, `400` o `404` |
| PATCH | `/api/tickets/{id}/assignment` | Asigna, reasigna o desasigna; `200`, `400`, `404` o `409` |
| DELETE | `/api/tickets/{id}` | Solo Admin; `204`, `404` o `409` si contiene comentarios |

### Permisos de tickets

| Operación | User | Technician | Admin |
| --- | --- | --- | --- |
| Listado y detalle | Creados por él | Creados por él o asignados a él | Todos |
| Crear | Propio | Propio | Propio |
| Cambiar estado | No | Solo asignados a él | Todos |
| PUT, prioridad, categoría, asignación y DELETE | No | No | Todos |

Los recuentos y filtros incluyen solo tickets visibles. El detalle ajeno devuelve 404. Las operaciones vetadas por rol devuelven 403; ausencia de JWT válido, 401 con challenge Bearer. Un técnico puede leer un ticket propio no asignado pero no cambiar su estado (403). Tickets históricos con creador NULL: solo Admin o técnico asignado. En POST se ignora `createdByUserId` del cuerpo y se toma siempre el ID del JWT validado y contrastado con PostgreSQL, también para Admin; la creación no asigna técnico.

### Comprobación manual de usuarios

Los GET de usuarios siguen disponibles. Para registrar, enviar `name`, `email` y una contraseña nueva elegida localmente a `/api/auth/register`. Introducirla desde un cliente que no guarde cuerpos sensibles en historial, logs ni capturas compartidas; no colocar contraseñas reales en comandos versionados. Se documenta el contrato y la validación en [AUTHENTICATION.md](docs/AUTHENTICATION.md).

POST/PUT de usuarios requieren Admin y conservan validaciones de campos, unicidad y restricciones de asignación. La [guía de pruebas manuales por rol](docs/AUTHENTICATION.md#prueba-manual-de-cada-rol-en-linux-mint-sin-escribir-datos) usa únicamente login y GET con cuentas existentes; no se ejecutaron solicitudes contra desarrollo en esta etapa.

### Crear y consultar un ticket

```http
POST /api/tickets
Content-Type: application/json

{
  "title": "El portátil no arranca",
  "description": "La pantalla permanece apagada al encender el equipo.",
  "priority": "High",
  "category": "Hardware"
}
```

Respuesta ilustrativa: `201 Created`, con `Location: /api/tickets/1`.

```json
{
  "id": 1,
  "createdByUserId": null,
  "assignedToUserId": null,
  "title": "El portátil no arranca",
  "description": "La pantalla permanece apagada al encender el equipo.",
  "createdAt": "2026-10-20T10:00:00Z",
  "status": "Open",
  "priority": "High",
  "category": "Hardware"
}
```

El ID y la fecha los genera el servidor. `GET /api/tickets/1` devuelve el mismo formato; el listado devuelve estos objetos dentro de `items`, junto con los metadatos de paginación.

`priority` y `category` son opcionales en POST: si se omiten, se asignan `Medium` y `Other`. Enviar `null` explícitamente no es válido. Los nombres de estados, prioridades y categorías se aceptan con la capitalización mostrada; los números y nombres desconocidos se rechazan.

### Creador y asignación — etapa 8.3

POST /api/tickets conserva `createdByUserId` en el DTO por compatibilidad, pero ignora su valor. Siempre registra como creador al usuario autenticado y deja AssignedToUserId en NULL. No se permite atribuir tickets a otro usuario, tampoco a Admin. Un valor de tipo JSON incompatible sigue produciendo 400 durante la deserialización.

```json
{
  "title": "Pantalla sin señal",
  "description": "Revisar el equipo.",
  "createdByUserId": 3
}
```

Todas las respuestas que contienen un ticket añaden `createdByUserId` y `assignedToUserId` (número o null). Las navegaciones de EF no se serializan. El listado conserva `{items,totalCount,page,pageSize,totalPages}`. PUT de tickets sigue editando solo Title/Description; ni este ni los PATCH de clasificación cambian el creador o la asignación.

Asignar o reasignar requiere una petición de Admin; el usuario destino debe ser Technician o Admin:

```http
PATCH /api/tickets/123/assignment
Content-Type: application/json

{"assignedToUserId": 5}
```

Desasignar requiere null explícito:

```json
{"assignedToUserId": null}
```

Campo omitido, tipo incorrecto, usuario inexistente o rol User: 400. Ticket inexistente con cuerpo válido: 404. Éxito: 200 con ticket completo; repetir la asignación válida o la desasignación no guarda cambios. Si un usuario desaparece entre la validación y el guardado, una violación de las nuevas FK devuelve 409. No cambia el estado ni hay restricciones adicionales por estado.

PUT /api/users/{id} requiere Admin y rechaza con 409 el cambio a User mientras existan tickets asignados. Las FK siguen siendo opcionales, independientes e indexadas, con ON DELETE RESTRICT y sin borrado en cascada.

La asignación mantiene la transacción ReadCommitted y FOR SHARE sobre el usuario destino hasta commit. Se permiten asignaciones a tickets distintos del mismo técnico. PUT utiliza FOR NO KEY UPDATE antes de comprobar asignaciones; ambos mantienen el bloqueo hasta finalizar la transacción. No hay garantía de rol frente a SQL externo que eluda la regla HTTP. Asignaciones simultáneas al mismo ticket siguen la última escritura.

### Revisar y aplicar AddTicketUserRelations manualmente

Migración `20261021120235_AddTicketUserRelations`: dos columnas integer NULL en Tickets, dos índices y dos FK a Users con RESTRICT. Los tickets anteriores conservan sus campos y reciben null en las nuevas relaciones; no hay backfill, usuarios ficticios ni reconstrucción de tabla. Down elimina las dos relaciones/columnas y pierde sus valores, conservando los demás datos de tickets.

Las consultas y relaciones de tickets necesitan ese esquema. La etapa 9.1 requiere además AddUserPasswordHash, cuya revisión previa está en [AUTHENTICATION.md](docs/AUTHENTICATION.md). Revisar el SQL y las migraciones pendientes; el DDL requiere bloqueos sobre Tickets durante su aplicación. Estos comandos son para revisión y ejecución manual del usuario; no se han aplicado migraciones en esta sesión:

```bash
dotnet ef migrations script AddUsers AddTicketUserRelations --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext --output /tmp/helpdesk-ticket-user-relations-review.sql
```

Ejecutar primero la suite PostgreSQL con la conexión segura de pruebas descrita en la guía. Tras revisar el resultado, respaldar la base de desarrollo y aplicar manualmente la migración durante una parada coordinada de la API:

```bash
dotnet ef database update AddTicketUserRelations --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext -- --environment Development
```

Comparar el número de tickets antes/después, comprobar los nuevos campos null en los tickets previos y verificar GET, creación, asignación, desasignación y rechazo de roles/IDs inválidos con datos de prueba propios. Los clientes con esquemas JSON estrictos deberán admitir los dos campos nuevos.

### Filtrar y paginar tickets

```http
GET /api/tickets?status=Open&priority=High&category=Hardware&search=portátil&page=1&pageSize=10
```

Todos los parámetros son opcionales y combinables. `status`, `priority` y `category` aceptan los nombres exactos documentados. `search` busca una subcadena literal en Title o Description sin distinguir mayúsculas/minúsculas; `%`, `_` y `\` se tratan como texto. Una cadena vacía no añade filtro; los espacios se conservan.

`assignedToMe` (fase 10.4) acepta exclusivamente `true` o `false`, en minúsculas y una sola vez. Con `true`, limita a `AssignedToUserId` igual al ID de la cuenta autenticada; con `false` u omitido conserva la visibilidad habitual (para Technician, creados por él o asignados a él). Solo Technician puede enviar este parámetro: User y Admin reciben 403 incluso con `false`. Valores vacíos, desconocidos o repetidos producen 400 antes de consultar SQL; la validación del valor precede al rechazo por rol. Sin autenticación válida se mantiene 401. El cliente no puede elegir otro técnico mediante este filtro; `assignedToUserId` no es un parámetro soportado. Los parámetros desconocidos se ignoran.

Ejemplo para Technician: `GET /api/tickets?assignedToMe=true&status=Open&priority=High&page=1&pageSize=10`. Se combina con el resto de filtros antes de calcular totales y paginar, sin cambiar el DTO ni la ordenación.

`page` es un entero positivo (por defecto 1); `pageSize` está entre 1 y 100 (por defecto 10). Valores vacíos, incorrectos o parámetros conocidos repetidos devuelven `400` con `{"error":"mensaje explicativo"}`. También se rechaza una combinación cuyo desplazamiento `(page - 1) * pageSize` supere `2147483647`.

**Cambio de contrato:** GET /api/tickets ya no devuelve un array; los clientes deben leer `items`:

```json
{
  "items": [],
  "totalCount": 0,
  "page": 1,
  "pageSize": 10,
  "totalPages": 0
}
```

Cada elemento conserva todas las propiedades del ticket. `totalCount` cuenta los resultados filtrados antes de paginar; `totalPages` es su división entre `pageSize` redondeada hacia arriba, o 0 sin resultados. Una página posterior a la última devuelve `items: []` conservando los totales. El orden es CreatedAt descendente y después Id descendente. El orden resuelve empates; las escrituras concurrentes pueden desplazar resultados entre peticiones de páginas.

EF Core ejecuta los filtros, la búsqueda PostgreSQL `ILIKE`, el conteo y la paginación en el servidor mediante consultas parametrizadas y `AsNoTracking`. No se cargan todos los tickets en memoria. Conteo y lectura son dos consultas: pueden observar cambios concurrentes entre ambas.

### Actualizar información y clasificación

PUT recibe únicamente los textos editables:

```http
PUT /api/tickets/1
Content-Type: application/json

{"title":"Portátil pendiente de revisión","description":"Se ha comprobado la alimentación del equipo."}
```

Los cambios de clasificación se realizan por separado:

```http
PATCH /api/tickets/1/status
Content-Type: application/json

{"status":"InProgress"}
```

```http
PATCH /api/tickets/1/priority
Content-Type: application/json

{"priority":"Critical"}
```

```http
PATCH /api/tickets/1/category
Content-Type: application/json

{"category":"Other"}
```

Cada actualización válida devuelve `200` con el ticket completo y los demás campos conservados. Por ejemplo, solicitar `Closed` para un ticket que sigue en `Open` devuelve `409`:

```json
{
  "error": "No se permite cambiar el estado de Open a Closed.",
  "currentStatus": "Open",
  "requestedStatus": "Closed"
}
```

Un cuerpo JSON incorrecto o incompatible con el DTO devuelve `400` con una respuesta breve:

```json
{"error":"El cuerpo JSON de la petición no es válido."}
```

## Ejecución local

### Requisitos

- SDK de .NET 10.
- PostgreSQL local; la configuración documentada utiliza PostgreSQL 16.
- Git y acceso a NuGet para restaurar dependencias.
- `psql` para preparar la base; Python 3 para los clientes de verificación. Las pruebas de terminal Linux usan `script` y `stty` de util-linux/coreutils, disponibles habitualmente en Linux Mint.

Los comandos se ejecutan desde la raíz del repositorio. Los ejemplos de terminal utilizan Bash.

```bash
git clone https://github.com/Ancodi1/HelpDeskt.NET.git HelpDesk.NET
cd HelpDesk.NET
dotnet restore HelpDesk.NET.slnx
dotnet tool restore --tool-manifest dotnet-tools.json
```

### PostgreSQL y User Secrets

Preparar manualmente una base de desarrollo `helpdesk_db` y un usuario dedicado `helpdesk_user`, con permisos para aplicar sus migraciones. La [guía de PostgreSQL](docs/POSTGRESQL.md) incluye los comandos de creación y comprobación de conexión con `psql`.

La API utiliza `ConnectionStrings:DefaultConnection`. El proyecto ya tiene `UserSecretsId`: no es necesario inicializarlo de nuevo. Guardar la cadena fuera de Git mediante User Secrets; este mecanismo es para desarrollo y no cifra los datos.

Formato de referencia, con un marcador que debe sustituirse localmente:

```text
Host=localhost;Port=5432;Database=helpdesk_db;Username=helpdesk_user;Password=<CONTRASEÑA_LOCAL>
```

Para guardarla sin mostrarla ni escribir su valor en el historial, usar entrada oculta y enviar JSON por stdin a Secret Manager:

```bash
python3 - <<'PY'
import getpass
import json
import subprocess

connection = getpass.getpass('Cadena de conexión local completa: ')
if not connection.strip():
    raise SystemExit('La cadena no puede estar vacía.')
result = subprocess.run(
    ['dotnet', 'user-secrets', 'set', '--project', 'HelpDesk.Api/HelpDesk.Api.csproj'],
    input=json.dumps({'ConnectionStrings:DefaultConnection': connection}),
    text=True, capture_output=True,
)
if result.returncode:
    raise SystemExit('No se pudo guardar la configuración local.')
print('Configuración local guardada.')
PY
```

La cadena debe respetar la sintaxis Npgsql, especialmente si la contraseña contiene punto y coma o comillas. No guardar credenciales en `appsettings*.json` ni compartir la salida de `dotnet user-secrets list`.

### Configuración JWT obligatoria

Antes de arrancar, configurar `Jwt:Issuer`, `Jwt:Audience` y `Jwt:SigningKeyBase64` fuera del repositorio. La [guía JWT para Linux Mint](docs/AUTHENTICATION.md#configuración-en-linux-mint) genera la clave criptográfica sin mostrarla y explica cómo probar el login con una cuenta existente sin escribir datos. La API falla al arrancar si falta configuración; no hay clave predeterminada. Las políticas protegen tickets y gestión de usuarios según la matriz anterior; el rol se contrasta en PostgreSQL en cada petición protegida.

### Migraciones y arranque

Con la base creada y la conexión configurada, revisar las migraciones antes de aplicarlas a una base con datos. Status, Priority y Category se añaden con valores predeterminados para los tickets anteriores: `Open`, `Medium` y `Other`.

```bash
dotnet build HelpDesk.NET.slnx
dotnet ef database update --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext -- --environment Development
dotnet run --project HelpDesk.Api --launch-profile http
```

El perfil `http` activa `Development`, carga User Secrets y escucha en `http://localhost:5085`. La API no aplica migraciones automáticamente al arrancar.

```bash
curl http://localhost:5085/api/health
```

Respuesta esperada: `{"status":"ok"}`. Este endpoint comprueba que la API responde; no verifica la conexión con PostgreSQL.

## Primer administrador (CLI)

La [guía de bootstrap para Linux Mint](docs/AUTHENTICATION.md#etapa-94-inicialización-del-primer-admin-mediante-cli) detalla configuración externa, confirmaciones y verificación posterior con login/GET sin alterar tickets. El comando independiente es `dotnet run --project HelpDesk.AdminBootstrap --no-build --no-launch-profile`; requiere una terminal interactiva y `HELPDESK_BOOTSTRAP_CONNECTION_STRING` cargada mediante entrada oculta. No acepta contraseñas por argumentos ni usa User Secrets como alternativa automática.

El primer Admin de desarrollo ya está creado y verificado; no repetir esta operación sobre esa cuenta. Cualquier uso en otro destino requiere autorización explícita. Si existe cualquier Admin o el correo está registrado, rechaza sin cambiar cuentas. No hay recuperación de contraseñas ni activación de cuentas antiguas.

## Pruebas automatizadas

La suite actual contiene **538 casos, todos superados y 0 fallidos**, según los resultados confirmados por el usuario: **257 sin PostgreSQL y 281 con PostgreSQL**. Incluye JWT, autorización y bootstrap; una prueba real de pseudoterminal cancela antes de conectar y comprueba que la contraseña no aparece. El login y acceso administrativo están verificados; la suite PostgreSQL completa, incluida la carrera entre dos procesos CLI, está superada.

Las pruebas unitarias verifican transiciones, validaciones y mapeos. Una prueba HTTP de salud utiliza WebApplicationFactory y TestServer en memoria, sin arrancar Kestrel manualmente. Para ejecutar todos los casos que no necesitan base de datos:

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL'
```

### Integración con PostgreSQL aislado

Las pruebas del CRUD utilizan exclusivamente `helpdesk_test_db`, un usuario dedicado `helpdesk_test_user` y un servidor local. Requieren una cadena explícita en `HELPDESK_TEST_CONNECTION_STRING`, almacenada fuera del repositorio. No utilizan la conexión de desarrollo como alternativa.

Seguir primero la [guía de pruebas PostgreSQL](docs/POSTGRESQL_TESTS.md) para crear manualmente la base y el rol, configurar la variable y revisar sus permisos. La fixture valida el destino antes de conectar, comprueba su identidad y privilegios y aplica las migraciones existentes solo a la base de pruebas. Los tests no crean ni eliminan bases; preparan tickets con GUID y limpian únicamente sus propios registros.

Con esa configuración preparada:

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category=PostgreSQL'
```

Para ejecutar toda la suite con el mismo entorno seguro:

```bash
dotnet test HelpDesk.NET.slnx
```

Si falta la cadena o no supera la validación del destino, los casos PostgreSQL fallan antes de conectar. Para trabajar sin ella, utilizar el filtro de exclusión anterior.

## Próximos pasos

Previsión de evolución, pendiente de implementación:

- Evolución adicional de la gestión de usuarios, cuando se solicite.
- Recuperación/activación segura de cuentas existentes y evolución de la revocación de JWT, pendientes de solicitud.
- Ampliaciones de la interfaz web según las siguientes fases solicitadas.

## Documentación

- [Contraseñas, registro y revisión de la migración](docs/AUTHENTICATION.md).
- [Progreso y verificaciones](docs/PROGRESS.md).
- [Arquitectura y decisiones](docs/ARCHITECTURE.md).
- [Preparación local de PostgreSQL](docs/POSTGRESQL.md).
- [Pruebas con PostgreSQL aislado](docs/POSTGRESQL_TESTS.md).

Los registros fechados describen etapas anteriores. En particular, las referencias de la guía inicial a escrituras en memoria son históricas: el CRUD actual utiliza EF Core y PostgreSQL.


### Gestión desde el detalle — fase 10.3.3

Cada guardado ejecuta una sola operación. No hay guardado conjunto ni transacción entre formularios.

| Operación API | Campos enviados | Permiso |
|---|---|---|
| PUT /api/tickets/{id} | title, description | Admin |
| PATCH /api/tickets/{id}/status | status | Admin o Technician asignado |
| PATCH /api/tickets/{id}/priority | priority | Admin |
| PATCH /api/tickets/{id}/category | category | Admin |
| PATCH /api/tickets/{id}/assignment | assignedToUserId (entero o null explícito) | Admin |

User no puede editar. Technician puede leer un ticket propio sin estar asignado, pero no gestionar su estado. La API comprueba cuenta/rol actuales y acceso al recurso en cada petición. No se implementa DELETE aunque exista en el backend.

Título y descripción conservan la validación del POST: textos no blancos, título máximo 100 unidades UTF-16. Prioridad y categoría usan los enums existentes. Transiciones: Open → InProgress; InProgress → Open/Resolved; Resolved → Open/InProgress/Closed; Closed → Open. Reenviar el mismo estado está permitido. Una transición incompatible devuelve 409.

Desde la fase 10.5, Asignación carga el directorio administrativo y ofrece un selector con nombre y correo de Technician y Admin. El ID real se utiliza como valor interno; «Sin asignar» envía null. Se conserva la asignación actual al cargar. Si no está en el conjunto elegible, se muestra como no disponible y se exige elegir otra o desasignar, sin cambiarla silenciosamente. Un error de carga bloquea el envío de asignación y ofrece reintentar la lectura; no bloquea las otras operaciones. La API no informa de actividad ni activación de cuentas. El servidor devuelve 400 si el destino ya no existe o no tiene rol compatible, y 409 si deja de estar disponible durante el guardado. Todas las escrituras devuelven 200 con el Ticket actualizado; también pueden devolver 400/401/403/404 según validación, autenticación y acceso.

El formulario bloquea envíos simultáneos, confirma el éxito y actualiza el detalle con la respuesta validada. Tras un error exige «Actualizar detalle» antes de reenviar. No hay reintentos automáticos; un fallo de conexión, timeout o respuesta ilegible puede ocurrir después de guardar. No hay control de versión: las modificaciones administrativas concurrentes conservan la política del backend de última escritura. Los filtros y la página siguen en la URL al volver al listado.

#### Comprobación manual de edición

Estas comprobaciones escriben datos: usar únicamente una API conectada a un destino aislado autorizado y ya preparado, con cuentas activas y tickets exclusivos de prueba. No cambiar conexiones versionadas ni ejecutar migraciones. Usar el arranque aislado descrito en autenticación y confirmar el destino antes de continuar.

1. Arrancar la API con `dotnet run --project HelpDesk.Api --launch-profile http` y, en otra terminal desde HelpDesk.Web, `npm start`. Abrir http://localhost:4200/login e iniciar sesión como Admin con credenciales introducidas solo en el navegador.
2. Abrir `/tickets`, aplicar filtros/página y entrar en un ticket de prueba. Anotar su ID y valores iniciales. En «Gestionar ticket», elegir «Título y descripción». Probar vacío/solo espacios: no debe salir PUT. Guardar textos válidos: comprobar un único PUT, confirmación y cabecera/descripción actualizadas.
3. Elegir Prioridad y después Categoría; guardar cada una. Comprobar PATCH independiente con un solo campo y respuesta 200, badges/datos actualizados y persistencia al pulsar «Actualizar detalle».
4. Elegir Estado. Recorrer Open → InProgress → Resolved → Closed → Open, guardando cada paso. Solo deben aparecer el actual y los destinos permitidos. Para comprobar 409 con datos de prueba, cambiar el estado desde otra sesión Admin después de cargar el detalle y enviar una transición que haya dejado de ser válida; actualizar el detalle tras el conflicto.
5. Elegir Asignación. Seleccionar una cuenta Technician de prueba por nombre y correo, guardar y verificar el responsable devuelto. No deben ofrecerse cuentas User. Actualizar el detalle y comprobar la selección conservada; elegir «Sin asignar» y guardar: debe enviar null. Repetir con un Admin de prueba como destino válido. Ante una lista vacía o un responsable que ya no sea elegible, comprobar el aviso y la posibilidad de desasignar explícitamente. Los rechazos por elegibilidad obsoleta están cubiertos con HTTP simulado; no modificar usuarios reales para provocarlos.
6. Asignar el ticket al Technician de prueba. Cerrar sesión e iniciar como ese Technician: debe aparecer solo la gestión del estado. Como Technician creador pero no asignado, no debe aparecer gestión. Como User propietario, debe verse el detalle sin edición. Los tickets ajenos no visibles deben responder 404.
7. Durante un guardado, comprobar campos/botón desactivados y un solo envío incluso con doble clic. En herramientas del navegador, simular desconexión para comprobar el mensaje de resultado incierto y ausencia de reintentos; reconectar y pulsar «Actualizar detalle» antes de operar.
8. Volver al listado mediante el enlace y comprobar filtros/página. Revisar el detalle en móvil (por ejemplo 390 px), etiquetas, controles y navegación. No copiar JWT ni credenciales a informes.

La revisión visual autenticada y estas escrituras manuales quedan pendientes de ejecución por el usuario; las pruebas automatizadas simulan HTTP y no modifican tickets reales.

### Administración — fase 10.5

La ruta `/admin` y el enlace **Administración** están disponibles solo para Admin. User y Technician se redirigen a `/session`; sin sesión se vuelve a `/login` antes de cargar datos. Los guards son controles de navegación: la API sigue comprobando JWT, cuenta y rol actuales en cada petición.

El resumen muestra todos los estados, incluido Closed, con cuatro consultas a `GET /api/tickets?status=<estado>&page=1&pageSize=1`, leyendo `totalCount`. Cada tarjeta abre el listado existente filtrado por estado. No cuenta las filas de una página ni usa assignedToMe. Son consultas independientes, sin instantánea global atómica; pueden reflejar cambios concurrentes. Resumen y directorio tienen carga, error y actualización independientes.

| Contrato utilizado | Datos y alcance |
| --- | --- |
| `GET /api/users` | Directorio completo ordenado por ID, reservado a Admin: id, name, email, role, createdAt. Sin paginación ni filtros de servidor; la búsqueda por nombre/correo/ID y el filtro por rol se aplican al conjunto completo en memoria. |
| `PUT /api/users/{id}` | Envía exclusivamente name/email/role; normaliza solo el correo. Devuelve el DTO público confirmado. Nombre no blanco de hasta 100 unidades UTF-16, correo con dominio completo de hasta 254 y rol User/Technician/Admin. El servidor conserva la validación definitiva. |
| `PATCH /api/tickets/{id}/assignment` | Reutilizado sin cambios: assignedToUserId real de Technician/Admin o null para desasignar. Directorio solicitado solo al abrir Asignación como Admin. |

Cambiar el rol requiere confirmación explícita. Pasar a User con tickets asignados produce 409; el correo duplicado también produce 409. Desde 10.6.1, **la API rechaza degradar al último Admin con 409 y código `last_admin`**, y Angular muestra un mensaje específico sin exponer cuerpos del servidor. Cambiar el propio rol con éxito cierra deliberadamente la sesión y vuelve al login; cualquier cambio de rol exige un nuevo login del usuario afectado. Un cambio rechazado conserva la sesión y exige actualizar el directorio antes de reenviar. Cambiar nombre/correo o editar otra cuenta no provoca logout. No se alteran las reglas JWT existentes.

Los formularios bloquean envíos duplicados. Errores 400/401/403/404/409 o resultados inciertos muestran mensajes seguros; 401 lo resuelve el interceptor, 403 conserva la sesión. Tras un fallo de escritura se exige actualizar el directorio antes de editar de nuevo, sin reintentos automáticos. Recargar cancela lecturas obsoletas; cancelar una petición no garantiza rollback. No hay ETag ni protección frente a última escritura administrativa concurrente.

No hay creación ni eliminación de usuarios en este panel, edición de contraseñas ni activación. `POST /api/users` crea sin credencial y no existe activación segura; se conserva el registro público (`/register`, siempre User) y la posterior edición de rol por Admin. El DTO público no permite saber si una cuenta puede iniciar sesión: no se deduce ese estado ni se solicitan hashes. El directorio completo limita la escalabilidad; paginarlo requeriría un contrato de backend autorizado por separado.

#### Comprobación manual con Admin

1. Usar una API configurada externamente para una base aislada ya preparada y autorizada, una cuenta Admin existente con credenciales conocidas y usuarios/tickets exclusivos de prueba. No ejecutar migraciones ni usar datos reales para probar escrituras. Arrancar API y Angular como se indica al inicio.
2. Iniciar sesión en `/login`, abrir **Administración** y comprobar sus cuatro contadores. En la pestaña de red deben verse cuatro GET de tickets con pageSize=1 y un GET de usuarios. Abrir una tarjeta y comprobar el filtro de estado en Tickets.
3. Volver a Administración, buscar por nombre/correo y filtrar por rol. Comparar con el directorio completo. Comprobar estados vacío/sin coincidencias y presentación móvil a 390 px, sin compartir credenciales ni JWT.
4. Editar una cuenta de prueba: primero nombre vacío o demasiado largo/correo incorrecto (sin PUT), después valores válidos. Verificar un solo PUT, confirmación y datos devueltos por el servidor. Cambiar su rol, cancelar la confirmación y comprobar que no escribe; después confirmar un cambio autorizado y comprobar la actualización. No degradar al último Admin operativo para probar esta advertencia.
5. Con una cuenta de prueba que tenga asignaciones, intentar pasar a User: esperar 409 y actualizar el directorio antes de continuar. Esta prueba requiere autorización para ese entorno y esa cuenta. Si necesitas una cuenta con credencial, usar el registro público y después su cambio de rol; no POST /api/users como sustituto de activación.
6. Desde Tickets, abrir un ticket de prueba y seleccionar Asignación. Comprobar nombres/correos, selección actual, Technician/Admin y Sin asignar. Verificar PATCH de un único campo y detalle actualizado. Probar las otras operaciones del detalle para comprobar que siguen disponibles.
7. Cerrar sesión y entrar como User/Technician: el enlace administrativo debe desaparecer y `/admin` debe volver a `/session`. Technician conserva su panel y la edición de estado de tickets asignados. La suite automatizada cubre además 401/403, confirmación de cambio del propio rol y errores de escritura sin utilizar datos reales.

Validación de 10.5: builds Angular y .NET correctos; 320 pruebas Angular y 265 .NET sin PostgreSQL superadas, cero fallidas. Comprobación visual autenticada pendiente. Sin cambios de backend, dependencias, migraciones ni datos reales.

### Protección del último administrador — fase 10.6.1

`PUT /api/users/{id}` no permite pasar de Admin a User o Technician cuando no existe otra cuenta Admin. El rechazo es atómico: tampoco guarda el nombre o correo enviados en esa petición. Se permite editar al único Admin manteniendo su rol y degradar una cuenta cuando queda otro Admin, siempre respetando la validación de datos, correo único y la prohibición de pasar a User con tickets asignados. Se conservan 400/401/403/404 y los demás conflictos 409.

```json
{
  "error": "No se puede cambiar el rol del último administrador.",
  "code": "last_admin"
}
```

El campo `code` es una ampliación aditiva del error 409; las demás respuestas y DTOs no cambian. Angular reconoce únicamente ese código conocido y genera un mensaje local: no refleja el texto recibido. Se conserva la confirmación previa, el formulario intentado y la sesión Admin tras el rechazo. Se desactiva el guardado hasta actualizar el directorio y revisar el estado real, sin reintentos de escritura.

La transacción ReadCommitted obtiene `LOCK TABLE public."Users" IN SHARE ROW EXCLUSIVE MODE` antes de leer/bloquear la fila con FOR NO KEY UPDATE. Mantiene ambos bloqueos hasta commit/rollback. Este modo, ya usado en bootstrap, serializa los PUT entre instancias de la API y bloquea temporalmente escrituras de usuarios, incluido registro o rehash de login, sin bloquear lecturas ordinarias. Después de esperar, la consulta que busca otro Admin ve los cambios confirmados por la transacción anterior. Dos degradaciones concurrentes de los dos últimos Admin no pueden superar juntas la comprobación. Se mantiene el orden usuario → ticket del protocolo de asignación.

El alcance es conservar al menos una fila con rol Admin mediante la API, no asegurar que esa cuenta tenga credencial utilizable. Se cuentan también Admin sin contraseña, conforme al modelo existente; no se consultan hashes para tomar la decisión. No hay endpoints de eliminación/desactivación de usuarios. SQL directo u otros programas que modifiquen roles fuera de la API pueden eludir la regla después de liberar el bloqueo. No se repara una base que ya carezca de Admin ni se cambian autenticación, esquema o dependencias.

#### Comprobación manual de la protección

1. Utilizar exclusivamente una API conectada a una base aislada, ya preparada y autorizada, con cuentas y tickets de prueba. No ejecutar migraciones ni degradar administradores de desarrollo/producción para preparar el caso.
2. Con un único Admin de prueba, entrar en `/admin`, intentar cambiar su rol a Technician y confirmar. Esperar 409 con `code: "last_admin"`, mensaje específico, permanencia en `/admin` y sesión válida. El formulario conserva el intento y bloquea el reenvío hasta pulsar **Actualizar directorio**. Repetir hacia User usando una cuenta sin asignaciones para aislar la regla.
3. Actualizar el directorio: el usuario debe seguir siendo Admin y conservar sus campos. Editar su nombre manteniendo Admin debe funcionar. No debe iniciarse ningún PUT automáticamente al recargar.
4. En un destino de prueba que ya tenga dos Admin, degradar uno mediante el otro: esperar 200 y comprobar que queda un Admin. Intentar degradar el restante: esperar 409. Si la cuenta degradada es la propia, el éxito sí requiere volver a iniciar sesión; el rechazo nunca cierra sesión.
5. Comprobar que User/Technician siguen sin poder administrar usuarios y que asignación, estados y consultas de tickets mantienen sus permisos. Los casos de carrera se preparan en `TicketEndpointsTests.LastAdmin.cs` y observan `pg_blocking_pids`; una prueba manual de clics simultáneos no sustituye esa validación.

Validación ejecutada: 328 pruebas Angular y 265 .NET sin PostgreSQL superadas, más **301 pruebas PostgreSQL superadas en Docker exclusivo**, incluidas las doce nuevas y la concurrencia real. Cero fallos/omitidas, interbloqueos o transacciones pendientes. Ambos builds repetidos correctamente y diff sin errores de whitespace. Las siete migraciones se aplicaron únicamente a la base aislada autorizada; ningún acceso a datos habituales. Contenedor de pruebas detenido, volumen conservado. Procedimiento e identidad del entorno en [POSTGRESQL_TESTS.md](docs/POSTGRESQL_TESTS.md). Revisión visual autenticada pendiente; sin commit ni push.

### Comentarios — fase 10.6.2.1

El detalle incluye **Comentarios → Abrir comentarios**, con carga bajo demanda y paginación independiente. Cualquier cuenta autorizada a consultar el ticket puede leer y publicar, también si está cerrado: User creador, Technician creador o asignado y Admin. Cada petición comprueba visibilidad en servidor; conocer un ID no concede acceso. No hay comentarios privados, adjuntos, edición ni eliminación individual. El historial de cambios se incorpora en la fase 10.6.2.2 descrita a continuación.

| Contrato nuevo | Comportamiento |
| --- | --- |
| `GET /api/tickets/{id}/comments?pageSize=20&beforeId=<ID>` | `200` con `{items,hasMore,nextBeforeId,pageSize}`. Orden ID descendente, límite por defecto 20 y máximo 100. Cursor opcional positivo; solo devuelve IDs menores que beforeId y del ticket autorizado. Parámetros conocidos repetidos/vacíos/incorrectos: 400. |
| `POST /api/tickets/{id}/comments` | Cuerpo `{content}`; `201` con el comentario confirmado. ID, autor, nombre histórico y fecha los decide el servidor. Sin reintento automático ni idempotencia adicional. |

DTO de comentario: `{id,ticketId,authorUserId,authorName,content,createdAt}`. El nombre es una instantánea del perfil al publicar, sin correo ni rol; un cambio posterior del nombre no reescribe comentarios. Fecha UTC presentada en hora local. El contenido se conserva sin recortar, se muestra como texto escapado con saltos de línea y exige texto no blanco de hasta **4.000 unidades UTF-16**, sin caracteres nulos (PostgreSQL no los admite en text). Hay validación de aplicación y restricciones de no blanco/longitud UTF-16 en base de datos, incluidas las parejas de sustitutos de emojis.

Sin JWT válido: 401; sesión con cuenta/rol no válido: tratamiento 403 existente. Ticket invisible e inexistente comparten 404 sin datos. Validación: 400; conflictos de persistencia: 409 seguro. Las respuestas no incorporan correos, hashes, tokens ni navegaciones de entidades.

La lectura mantiene FOR SHARE del ticket durante la comprobación de visibilidad y materialización de la página. Publicar usa ReadCommitted y FOR UPDATE antes de comprobar visibilidad e insertar, coordinándose con reasignaciones y borrados. Si el técnico pierde visibilidad antes de adquirir el bloqueo, recibe 404 y no publica. Los cursores no se desplazan por nuevas publicaciones; no se calculan totales de toda la conversación. El último bloque sin más resultados devuelve nextBeforeId=null.

**No se permite borrar un ticket con comentarios:** DELETE administrativo devuelve 409 con `code: "ticket_has_comments"`, conservando ticket y conversación. El DELETE toma FOR UPDATE y comprueba dependencias dentro de la misma transacción; las FK Restrict impiden también el borrado SQL referenciado, sin cascada. Un ticket sin comentarios ni actividades conserva su borrado 204. La fase 10.6.2.2 amplía esta protección al historial.

El formulario bloquea duplicados y «Actualizar detalle» durante una publicación. Tras un fallo conserva el borrador, exige actualizar/revisar comentarios antes de reenviar y no refleja mensajes internos del servidor. Las denegaciones eliminan la conversación cargada. Cambiar de ticket cancela peticiones y limpia el contexto anterior. Cancelar o agotar un timeout no garantiza rollback: revisar los comentarios recientes antes de repetir el POST.

#### Validación manual de comentarios

1. Utilizar una API configurada externamente **solo para el PostgreSQL aislado** con AddTicketComments aplicada y cuentas de prueba; arrancar Angular con `npm start`. La base habitual no se ha migrado. No apuntar una comprobación de escrituras al perfil habitual por omisión ni mezclar datos manuales con una suite en ejecución.
2. Entrar con una cuenta de prueba y abrir un ticket visible. Pulsar **Abrir comentarios**: comprobar carga, estado vacío o listado con autor histórico, fecha local y contenido. La apertura del detalle por sí sola no solicita la conversación.
3. Probar espacios, más de 4.000 unidades UTF-16 y emojis que superen ese límite: no debe publicarse. Escribir texto con saltos de línea y marcas como `<b>texto</b>`: debe verse literalmente, nunca como HTML. Publicar una vez y comprobar confirmación y formulario limpio; un doble envío pendiente no debe duplicar el POST.
4. Con más de 20 comentarios de prueba, usar **Cargar anteriores** y comprobar orden, cursor y ausencia de duplicados. Publicar otro desde una segunda sesión y comprobar que actualizar muestra las entradas nuevas. Si se pierde la conexión al publicar, actualizar antes de reenviar.
5. Con Technician asignado y otro Admin de prueba, reasignar a otro técnico: la siguiente lectura/publicación del anterior debe dar 404 si tampoco es creador. User ajeno tampoco debe consultar/publicar; un ticket cerrado visible sí permite comentarios.
6. Como Admin, intentar `DELETE /api/tickets/<ID_DE_PRUEBA_CON_COMENTARIOS>` desde un cliente local autenticado: esperar 409 y comprobar que ticket y comentarios permanecen. No hay botón de borrado en Angular ni endpoint para limpiar comentarios individuales. Preparar datos manuales solo en un destino exclusivo; no borrar globalmente registros para reiniciar las suites.

Validación automatizada ejecutada: **280 .NET sin PostgreSQL, 338 PostgreSQL y 367 Angular superadas**, cero fallidas/omitidas. Incluye 37 casos nuevos PostgreSQL y seis órdenes/casos de concurrencia observados con pg_blocking_pids. Ambos builds correctos, sin advertencias. `AddTicketComments` es una migración aditiva generada y aplicada exclusivamente al Docker de pruebas (ocho migraciones totales), nunca a la base habitual. Procedimiento en POSTGRESQL_TESTS.md; revisión visual autenticada pendiente. Sin nuevas dependencias, cambios JWT, commit ni push.

### Historial de actividad — fase 10.6.2.2

El detalle incorpora **Historial de actividad → Abrir historial**, independiente de los comentarios. Registra automáticamente cambios efectivos de estado, prioridad, categoría y responsable (incluida la desasignación). Muestra autor histórico, fecha/hora local, campo y valores anterior/nuevo traducidos; las asignaciones conservan los nombres históricos sin consultar el directorio. Los textos se interpolan de forma segura y el diseño reutiliza el de comentarios.

**El historial comienza desde esta implementación: no reconstruye cambios anteriores.** Crear tickets, editar título/descripción o publicar comentarios no genera actividades. No hay edición/eliminación de actividades, WebSockets ni sondeo. Repetir un valor, rechazar permisos/validación o revertir una operación no deja eventos.

`GET /api/tickets/{id}/activities?beforeId=<ID>&pageSize=20` requiere autenticación y usa `TicketAccess`: User creador, Technician creador o asignado, Admin todos. Devuelve 404 vacío tanto para tickets inexistentes como invisibles. Cursor positivo exclusivo, orden por ID descendente, tamaño por defecto 20/máximo 100; parámetros conocidos inválidos o repetidos devuelven 400. Respuesta `{items,hasMore,nextBeforeId,pageSize}`; cursor null al terminar. Cada elemento contiene `{id,ticketId,actorUserId,actorName,field,oldValue,newValue,oldAssigneeName,newAssigneeName,createdAt}`, sin correo, rol, credenciales ni navegaciones. `field` es `Status`, `Priority`, `Category` o `AssignedToUserId`; los valores de asignación son IDs en texto o null, acompañados por instantáneas de nombres. No hay endpoints de escritura de historial.

La migración aditiva **20261104211800_AddTicketActivities** crea tabla, restricciones y FK Restrict hacia ticket/actor, con índice `(TicketId,Id)`. Cambio y evento comparten transacción ReadCommitted y bloqueo FOR UPDATE del ticket hasta commit. Las asignaciones bloquean primero los usuarios anterior/nuevo con FOR SHARE, por ID ascendente, y después el ticket; si la asignación observada cambió durante la espera, liberan bloqueos y reintentan antes de escribir (máximo tres intentos, después 409). Esto conserva los nombres confirmados y el orden usuario → ticket también al desasignar. GET mantiene FOR SHARE del ticket mientras verifica visibilidad y lee la página.

DELETE impide borrar tickets con comentarios (`409`, `code: "ticket_has_comments"`) o actividades (`409`, `code: "ticket_has_activities"`). Si hay ambos, prevalece el código de comentarios. Comprobación y borrado se realizan bajo el mismo bloqueo del ticket; las FK impiden cascadas. Sin ambas dependencias, mantiene 204. Los permisos JWT y la protección del último Admin permanecen vigentes.

Angular ofrece carga, vacío, error seguro, reintento y **Cargar anteriores**. Al cambiar de ticket o destruir el componente cancela la lectura y limpia el contexto; una modificación confirmada actualiza el historial abierto desde la primera página y cancela una lectura anterior pendiente. Un error del historial no inutiliza el detalle ni los comentarios. Las pruebas Angular simulan HTTP; la revisión visual autenticada sigue pendiente.

#### Validación manual del historial desde Angular

1. Usar únicamente una API conectada externamente a un **Docker exclusivo verificado**, con AddTicketActivities aplicada y cuentas/datos de prueba de User, Technician y Admin. No usar la conexión habitual ni mezclar datos manuales con suites automáticas. Las migraciones de esta implementación se aplicaron solo al Docker automático descrito en [POSTGRESQL_TESTS.md](docs/POSTGRESQL_TESTS.md); la base habitual no está actualizada.
2. Arrancar esa API en el destino del proxy y Angular con `cd HelpDesk.Web` y `npm start`. Con User, abrir un ticket propio y **Abrir historial**: un ticket sin cambios nuevos debe mostrar el estado vacío, aunque hubiera modificaciones anteriores a esta fase.
3. Con Admin, abrir el historial y cambiar prioridad, categoría y responsable; reasignar y desasignar. Comprobar actualización tras cada éxito, autor, fecha y pares anterior → nuevo. Repetir el valor no debe añadir actividad. Renombrar posteriormente al usuario desde administración no debe reescribir los nombres de actividades anteriores.
4. Con el técnico asignado, cambiar Open → InProgress → Resolved y comprobar su autoría; un técnico ajeno y un User no deben adquirir permisos de gestión. Tras reasignar, el antiguo técnico sin otra visibilidad debe recibir el mismo 404 que para un ticket inexistente.
5. Preparar más de 20 cambios válidos solo en ese destino manual: **Cargar anteriores** debe añadir páginas sin duplicados. Cambiar de ticket durante una carga no debe mostrar actividad del anterior. Comprobar distribución en escritorio/móvil y que nombres con caracteres HTML aparecen como texto.
6. Interrumpir temporalmente la API: debe aparecer un error recuperable del historial conservando el detalle ya mostrado. Restablecer y pulsar **Actualizar historial**. Comprobar también que los comentarios siguen cargándose/publicándose de forma independiente.
7. El borrado no tiene interfaz Angular: su 409 con comentarios/actividad y el 204 sin dependencias se verifican en las pruebas HTTP aisladas. No hace falta añadir un control de eliminación al frontend.

### Preparación de producción (10.7.4)

Imagen multi-stage con Angular/API en un origen, usuario sin privilegios y secretos
externos. Construcción: `docker build -t helpdesk:local .`. Migraciones separadas,
roles PostgreSQL, configuración de proxy/HTTPS y sondas en [DEPLOYMENT.md](docs/DEPLOYMENT.md).
No se ha publicado la aplicación. Consultar PROGRESS para las validaciones ejecutadas.

### CI y recuperación (10.7.5)

El workflow [CI](.github/workflows/ci.yml) valida .NET, Angular, imagen Docker e
integración PostgreSQL efímera con ensayo de backup/restauración. No despliega.
Comandos, guardias, permisos, recuperación y verificación remota pendiente en
[CI_RECOVERY.md](docs/CI_RECOVERY.md).
