# Pruebas de persistencia con PostgreSQL aislado

Estado vigente de cierre de 9.4, confirmado por el usuario: **538 pruebas superadas, 0 fallidas**, distribuidas en **257 sin PostgreSQL y 281 con PostgreSQL**. Build anterior sin errores ni advertencias; auditoría de aislamiento PostgreSQL superada. La suite corregida ya se ha ejecutado satisfactoriamente. Los registros de ampliaciones siguientes son históricos: sus menciones a pruebas pendientes describen aquellas sesiones, no el estado actual.

Para verificar las correcciones, cargar de forma oculta la conexión según esta guía, exclusivamente a helpdesk_test_db. El validador y la fixture existentes verifican destino y rol limitado antes de preparar el esquema; nunca usar DefaultConnection. Desde la raíz:

```bash
dotnet restore HelpDesk.NET.slnx
dotnet build HelpDesk.NET.slnx --no-restore
dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL'
dotnet test HelpDesk.NET.slnx --no-build --filter 'Category=PostgreSQL'
```

Para repetir solo los dos órdenes de concurrencia después de la suite completa:

```bash
dotnet test HelpDesk.NET.slnx --no-build --filter 'Category=PostgreSQL&FullyQualifiedName~TechnicianStatus_WaitsForConcurrentReassignmentAndThenRejectsOldAssignee|Category=PostgreSQL&FullyQualifiedName~StatusLock_CommitsAuthorizedStatusBeforeReassignment_ThenRejectsFormerTechnician'
```

Estas ejecuciones deben demostrar espera real, finalización sin interbloqueos, estado íntegro y rechazo del técnico tras perder la asignación. Descubrir pruebas no verifica estas garantías.

## Estado

Base exclusiva `helpdesk_test_db` y rol `helpdesk_test_user` auditados. El rol no tiene atributos privilegiados, membresías ni permisos de escritura sobre tablas, columnas o secuencias de desarrollo, tampoco mediante otros roles accesibles. La suite PostgreSQL completa (281 casos) ha sido ejecutada y confirmada por el usuario como superada. La conexión sigue siendo externa mediante `HELPDESK_TEST_CONNECTION_STRING`; no hay fallback a la base de desarrollo.

## Preparación manual

Un administrador debe crear **manualmente** una base independiente llamada exactamente `helpdesk_test_db` y un rol exclusivo `helpdesk_test_user`. El rol debe poder iniciar sesión con contraseña y administrar el esquema de esa base para aplicar las migraciones. No debe ser superusuario, tener CREATEDB o CREATEROLE, pertenecer a roles privilegiados ni recibir permisos sobre tablas de desarrollo. No reutilizar el usuario de desarrollo. Los tests no crean ni borran bases.

Ejemplo de operaciones administrativas manuales en psql, fuera del código de pruebas:

```sql
CREATE ROLE helpdesk_test_user LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
```

Usar `\password helpdesk_test_user` en psql para asignar una contraseña sin escribirla en comandos versionados. Después, crear manualmente la base:

```sql
CREATE DATABASE helpdesk_test_db OWNER helpdesk_test_user;
```

Si esos recursos ya existen, revisar propietario, permisos y membresías en lugar de repetir su creación. Deben ser recursos exclusivos de pruebas. La separación de permisos del rol complementa la validación del nombre de base; nunca concederle permisos de escritura sobre `helpdesk_db`.

Guardar la cadena completa fuera del repositorio y suministrarla solo mediante `HELPDESK_TEST_CONNECTION_STRING`. Formato orientativo, con marcadores:

```text
Host=localhost;Port=<puerto>;Database=helpdesk_test_db;Username=helpdesk_test_user;Password=<contraseña-de-pruebas>
```

Para cargarla sin que se muestre ni quede su valor literal en el historial de Bash:

```bash
read -r -s -p 'Cadena PostgreSQL de pruebas: ' HELPDESK_TEST_CONNECTION_STRING
export HELPDESK_TEST_CONNECTION_STRING
```

El validador acepta únicamente las claves Host, Port, Database, Username, Password, SSL Mode, Timeout, Command Timeout y Pooling, además de Include Error Detail, Persist Security Info y Enlist exclusivamente con false. Estas tres últimas también se añaden al normalizar la cadena y deben poder validarse otra vez en la fábrica. Host debe ser `localhost`, `127.0.0.1` o `::1`, y base y usuario deben coincidir exactamente. No admite destinos múltiples, Options, Search Path ni resolución implícita de base/usuario/contraseña. No imprimir la variable ni guardar archivos con credenciales en el repositorio.

## Ejecución

Pruebas sin PostgreSQL (el resultado vigente de etapa 9.3 figura al final: reglas, modelos, migraciones, hashing, JWT y protección HTTP sin acceder a datos):

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL'
```

Solo después de la preparación manual y de revisar la configuración:

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category=PostgreSQL'
```

La fixture valida la cadena **antes de conectar**, abre directamente la base exigida (falla si no existe), confirma `current_database()`, `current_user` y privilegios del rol mediante SELECT, y solo después llama a `MigrateAsync` con las migraciones existentes. El rol sin CREATEDB impide crear bases incluso si desapareciera el destino durante la preparación. Las migraciones modifican el esquema de pruebas y su historial, nunca el de desarrollo. No se usa EnsureCreated, EnsureDeleted ni una conexión administrativa.

`dotnet test HelpDesk.NET.slnx` sin filtro incluye los tests PostgreSQL: si falta configuración, fallan con un error explícito antes de conectar. No hay fallback a DefaultConnection ni omisiones silenciosas. Con configuración válida se pueden ejecutar todas las pruebas en ese mismo comando.

## Aislamiento y limpieza

`PostgreSqlFixture` comparte la fábrica y prepara el esquema una vez por clase. `TicketApiFactory` usa Testing y reemplaza HelpDeskDbContext, sus opciones y su configurador mediante ConfigureTestServices. El comportamiento de desarrollo no cambia. No hay nuevos paquetes ni proveedores alternativos.

xUnit crea una instancia de `TicketEndpointsTests` por caso. Cada una registra los títulos GUID de sus tickets antes de guardarlos y los IDs propios tras guardarlos. La limpieza asíncrona elimina solo esas filas por ID o título exacto y comprueba que ya no existen, incluso si falla una aserción o PUT cambia el título. Se conserva el título original del POST para limpiar aunque falle antes de leer la respuesta. No se vacían tablas, no se reinician secuencias ni se eliminan tickets de otros casos. Los tres casos originales comprueban POST 201 y persistencia con un contexto independiente, GET 200 de una fila preparada directamente en la base, y GET 404 de un ID previamente comprobado como ausente. Los 17 nuevos cubren PUT válido, título vacío/largo y 404; cuatro transiciones PATCH válidas, conflicto Open → Closed con sus datos, repetición de cada estado, estado inválido y 404; DELETE válido conservando un segundo ticket de control y DELETE 404. Tras cada modificación se comprueba GET y una consulta AsNoTracking en otro scope; para DELETE se confirma la ausencia tanto por HTTP como en la base. Los casos de error y repetición comprueban todas las propiedades sin cambios.

Si se interrumpe el proceso o se pierde la conexión, la limpieza no puede garantizarse: revisar manualmente solo los tickets `integration-<GUID>` de la ejecución afectada. No borrar globalmente por prefijo ni vaciar la base. Evitar ejecuciones simultáneas de la preparación de esquema; el aislamiento por GUID protege las filas, pero no coordina migraciones entre procesos distintos.

Los mensajes propios de configuración no incluyen cadenas ni contraseñas; la fábrica no registra parámetros SQL y desactiva sus proveedores de logs. No usar modos de diagnóstico que vuelquen variables de entorno o configuración sensible.

## Diagnóstico sin revelar secretos

La validación distingue mensajes de sintaxis ADO.NET incorrecta, opción no permitida, valor de opción inválido y restricciones de seguridad incumplidas. No incluye las claves desconocidas, sus valores ni excepciones internas. Un punto y coma sin citar puede separar accidentalmente la contraseña en otra opción o romper la sintaxis; no se puede reconstruir automáticamente la contraseña pretendida a partir de una cadena mal formada. Las comillas del shell y las comillas de la cadena ADO.NET son niveles distintos.

Para componer cadenas programáticamente, asignar Host, Database, Username y Password como propiedades de NpgsqlConnectionStringBuilder y obtener ConnectionString. El constructor aplica las comillas necesarias para conservar punto y coma, comillas y signo igual dentro del valor; no concatenar contraseñas. No imprimir el resultado ni la contraseña. La carga por read indicada arriba conserva el texto recibido, pero ese texto debe tener sintaxis ADO.NET válida.

Depuración del 20 de octubre: una prueba reprodujo el rechazo de la salida de Validate al volver a pasarla a Validate. La fixture normalizaba la cadena y la fábrica la validaba de nuevo; las opciones de seguridad añadidas no figuraban en la lista permitida. Corregido aceptándolas solo con false, manteniendo ambos controles. Las pruebas con contraseñas ficticias especiales pasan. La variable real no estuvo disponible en el proceso del agente; no se pudo inspeccionar su contraseña ni ejecutar los tres tests PostgreSQL. Este diagnóstico no atribuye a la contraseña real una causa no comprobada.

## Verificación de la ampliación PUT/PATCH/DELETE — 20 de octubre de 2026

Restore y build correctos (0 errores y 0 advertencias). Las 39 pruebas sin PostgreSQL pasan. Los 20 casos PostgreSQL se descubren con `dotnet test HelpDesk.NET.slnx --no-build --list-tests --filter 'Category=PostgreSQL'`; listar no ejecuta la fixture ni abre conexiones. La variable no está disponible en el proceso del agente, así que no se ha ejecutado ningún caso PostgreSQL en esta ampliación y no se declaran superados.

Todos los casos permanecen en TicketEndpointsTests y comparten la fixture existente; xUnit no los ejecuta simultáneamente dentro de la misma colección. Los GUID/IDs por instancia y scopes independientes evitan compartir datos mutables con otras pruebas. Continúa pendiente ejecutar las 20 pruebas con configuración segura y comprobar su limpieza real. El proceso abortado o una conexión perdida todavía pueden impedir la limpieza.

## Prioridades — 20 de octubre de 2026

La suite define ahora 103 casos: 54 sin PostgreSQL y 49 PostgreSQL. Se mantienen los 20 casos anteriores de base y se añaden 29 de prioridades en TicketEndpointsTests.Priority.cs, como parte de la misma clase parcial y su limpieza. Se comprueba la prioridad Medium por omisión, las cuatro prioridades explícitas, persistencia, errores JSON/validación, 404, repetición sin cambios y conservación de otras propiedades. Las comparaciones de los tests anteriores incluyen Priority.

AddTicketPriority está generada y revisada, sin aplicar en esta sesión. Al ejecutar la suite PostgreSQL con la configuración validada, la fixture prepara helpdesk_test_db con las migraciones existentes, incluida esta nueva migración. La variable no estaba disponible aquí: únicamente se compilaron/listaron los 49 casos, sin conectar ni declararlos superados. Las 54 pruebas sin PostgreSQL sí se ejecutaron y pasaron. No se aplicó ninguna migración sobre desarrollo; la revisión y aplicación manual de AddTicketPriority a desarrollo son un paso separado.

## Categorías — 20 de octubre de 2026

La suite define 151 casos: 70 sin PostgreSQL y 81 PostgreSQL. Los 32 nuevos de categorías están en TicketEndpointsTests.Category.cs, en la misma clase parcial y con la misma fixture y limpieza por GUID/ID. Verifican las cinco categorías en POST y PATCH, valores inválidos/null/números, JSON incorrecto, idempotencia, 404, GET y conservación de propiedades ajenas. El POST anterior comprueba Other al omitir categoría; las comparaciones comunes incluyen Category.

AddTicketCategory está generada y revisada, sin aplicar en esta sesión: ADD Category text NOT NULL DEFAULT 'Other', sin recrear tablas. La fixture la aplicará a helpdesk_test_db solo al ejecutar los tests con configuración validada. La variable no está disponible en el proceso del agente, así que se ejecutaron las 70 pruebas sin PostgreSQL y se listaron los 81 casos de base sin ejecutarlos. No se declaran pasados estos últimos ni se accedió a desarrollo. La aplicación manual a desarrollo queda pendiente de revisión; el modelo actualizado requiere que exista la columna.

## Listado filtrado y paginado — 21 de octubre de 2026

Añadidos 46 casos HTTP en TicketEndpointsTests.List.cs. Total: 197 casos, 70 sin PostgreSQL y 127 PostgreSQL. El listado devuelve ahora TicketPageDto con items, totalCount, page, pageSize y totalPages; no devuelve un array. Las pruebas anteriores consultan tickets individuales y no requieren adaptar su deserialización.

Los nuevos casos verifican filtros y combinación, búsqueda en título/descripción con capitalización distinta, comodines literales y texto parecido a SQL, ausencia de resultados, orden con empates de CreatedAt, páginas consecutivas sin duplicados, totales, límite de 100, errores de parámetros y desbordamiento del desplazamiento. Los conjuntos filtrados usan GUID exclusivos. La prueba sin filtros compara el listado predeterminado con una lectura ordenada del contexto sin modificar filas ajenas; requiere que no haya escrituras externas simultáneas entre ambas lecturas. Los 81 casos previos permanecen como regresiones del resto de endpoints.

Se conserva íntegra la infraestructura y la limpieza por títulos exactos/IDs propios. No se crean migraciones ni se cambia el esquema. Restore/build correctos; 70 pruebas sin PostgreSQL ejecutadas y superadas; 127 PostgreSQL descubiertas sin ejecución. HELPDESK_TEST_CONNECTION_STRING ausente: no se ha conectado a ninguna base. Pendiente ejecutar el comando PostgreSQL con la configuración segura descrita arriba y comprobar la limpieza real.

## Usuarios y roles — 21 de octubre de 2026

Catorce casos PostgreSQL nuevos en TicketEndpointsTests.Users.cs reutilizan la misma clase parcial y fixture: no se crean endpoints para probar persistencia. Se cubren roles como texto, default SQL, fecha UTC, límites de longitud/NOT NULL, unicidad al insertar y actualizar, y rechazo del correo no canónico por SQL directo. Tres casos mantienen un ticket propio como control y lo comprueban mediante GET y contexto independiente. Los 127 casos HTTP anteriores siguen como regresiones.

Cada prueba registra correos GUID exactos antes de escribir e IDs de sus entidades al generarse, incluyendo variantes de correo usadas en comprobaciones SQL. Dispose elimina solo esas filas y verifica su ausencia; la limpieza de tickets sigue en finally aunque falle la de usuarios. No se borran conjuntos globales ni se reinician secuencias. Siguen vigentes los límites de limpieza ante interrupción/pérdida de conexión.

AddUsers se generó y su SQL se revisó sin aplicarlo. Solo crea Users y su índice; al ejecutar esta suite con configuración segura la fixture aplicará las migraciones pendientes únicamente a helpdesk_test_db. No se cambia la fixture, fábrica ni validador de seguridad. Conexión ausente en esta sesión: no se ejecutaron las 141 pruebas PostgreSQL ni se comprobó su limpieza real. Las 79 pruebas sin base pasaron. Total definido: 220 casos.

## Endpoints de usuarios — etapa 8.2

El desarrollador comunica los 220 casos de la etapa anterior superados. Esta ampliación añade 32 casos sin base y 66 HTTP PostgreSQL en TicketEndpointsTests.UserHttp.cs. Total definido: 318 (111 sin base y 207 PostgreSQL).

Los nuevos casos cubren los cuatro endpoints, roles/default, errores de cuerpo/validación, límites 100/254, listado determinista, persistencia en scopes independientes, conservación de Id/CreatedAt, duplicados exactos/normalizados, actualización del propio correo y ausencia de cambios ante errores. Se mantienen los tests de tickets; un nuevo listado comprueba además un ticket propio de control.

Dos tests de concurrencia crean una fábrica derivada de fixture.Factory que añade exclusivamente un interceptor de SaveChanges. Una barrera deja pasar las dos comprobaciones previas antes de guardar, de modo que PostgreSQL debe decidir el conflicto real: una respuesta correcta y una 409 en POST/POST y PUT/PUT. La barrera tiene timeout de 15 segundos; no se sustituye proveedor ni configuración de conexión y no se vuelve a preparar el esquema en esa fábrica. Se verifica persistencia del ganador y, en PUT, que el perdedor conserva sus datos.

Se registran los correos GUID antes de enviar solicitudes, incluyendo los nuevos de PUT, y se utiliza la limpieza propia existente. No se modifican fixture, fábrica base, validador ni seguridad. No hay nuevas migraciones. Conexión de pruebas ausente en esta sesión: no se ejecutan PostgreSQL ni peticiones manuales contra desarrollo. Los comandos manuales del README escriben datos y quedan para revisión del usuario.

Verificación real: restore/build correctos sin advertencias, 111 casos sin PostgreSQL superados y 207 casos PostgreSQL descubiertos sin ejecución. git diff --check correcto. Los 220 casos comunicados por el desarrollador corresponden a la etapa anterior; no se atribuye ese resultado a una ejecución de PostgreSQL en esta sesión.

## Relaciones y asignación — etapa 8.3

El desarrollador comunica las 318 pruebas anteriores superadas. Se añaden nueve sin PostgreSQL y 38 PostgreSQL en TicketEndpointsTests.Relations.cs. Total: 365 (120 sin PostgreSQL y 245 PostgreSQL). La conexión segura está ausente en esta sesión: únicamente se ejecutaron las 120 sin base y se descubrieron los 245 casos PostgreSQL. No se abrió ninguna conexión ni se aplicaron migraciones.

La fixture existente aplicará AddTicketUserRelations exclusivamente a helpdesk_test_db al ejecutar la suite con configuración validada. La migración y SQL se revisaron antes, sin DDL sobre desarrollo. Las comprobaciones sin base verifican que Up solo añade columnas anulables, índices y FK RESTRICT. No se ha demostrado en esta sesión la migración sobre filas históricas reales; sus operaciones son aditivas y los tests cubren tickets creados con el formato anterior, con ambos IDs null.

Integración cubre creadores por rol, creación anterior/null, errores, asignación a Technician/Admin, rechazo a User/inexistente, reasignación/desasignación, idempotencia, campos protegidos, persistencia de IDs/navegaciones, filtros y paginación combinando tickets relacionados y sin relaciones, FK y rechazo del borrado SQL de usuarios referenciados. El borrado del test se limita a su usuario GUID propio y no añade ningún endpoint DELETE.

Dos tests pausan el guardado después de la validación y eliminan un usuario propio todavía sin referencias para comprobar 409 de POST/assignment por FK. Otro pausa la asignación, cambia el rol por HTTP y comprueba el límite conocido: las comprobaciones previas sin bloqueo común pueden dejar User con un ticket asignado. El test documenta el comportamiento actual, no una garantía de atomicidad. Los interceptores viven en fábricas derivadas de la segura existente, con timeout de 15 segundos; no cambian conexión, preparación del esquema ni validación de destino.

La limpieza se reordena obligatoriamente: primero los tickets por IDs/títulos propios y después los usuarios propios por IDs/correos exactos en finally. No se borran referencias de tickets ajenos ni se amplía ningún filtro global. Si la limpieza de tickets falla, la FK puede impedir también la de usuarios; se comunica el fallo y siguen vigentes los límites ante abortos y pérdida de conexión. No se debilitan las restricciones para limpiar.


## Registro y contraseñas — etapa 9.1

Estado actual: 137 pruebas sin PostgreSQL superadas y 217 PostgreSQL descubiertas sin ejecutar por conexión segura ausente. El usuario comunicó 375 pruebas superadas antes de esta etapa. El total actual es 354: se sustituyen los contratos de escritura pública de usuarios por 403 y las pruebas de registro, sin skips ni bypass de Testing.

AddUserPasswordHash añade únicamente PasswordHash text nullable. La fixture validada la aplicará solo a helpdesk_test_db al ejecutar la suite con configuración segura; esta sesión no la aplica. Los 39 casos PostgreSQL nuevos incluyen 34 de registro (también campos extra de rol/hash, normalización, validación, privacidad/logs, duplicados y carrera real con barrera), cuatro de prohibición de gestión y uno de migración sobre tablas temporales con datos anteriores y FK. Este último genera el SQL de Up con el proveedor y compara filas/relaciones antes/después sin revertir ni modificar las tablas compartidas.

POST y PUT de usuarios están deshabilitados en todos los entornos. Se conservan las preparaciones de usuarios por EF en la base exclusiva de pruebas, GET, persistencia y relaciones; los casos de escritura de las etapas anteriores y carreras dependientes de cambiar roles por HTTP se sustituyen por rechazo 403 y conservación. Assignment mantiene su transacción y los tests concurrentes de asignaciones compatibles/borrado.

Las contraseñas válidas de prueba se generan durante la ejecución; ni estas ni sus hashes se imprimen. La comprobación de logs usa un proveedor de captura solo en la fábrica derivada del test. No activar logging de cuerpos ni EnableSensitiveDataLogging. Las cuentas anteriores con NULL no se reclaman por el registro. Revisar AUTHENTICATION.md y el SQL incremental antes de cualquier aplicación manual a desarrollo.


## Autorización por roles y recursos — etapa 9.3

Suite actual: 489 casos definidos, 216 sin PostgreSQL y 273 PostgreSQL. Los apartados fechados anteriores son históricos; POST/PUT administrativos se reactivan solo para Admin en esta etapa.

Las regresiones de tickets, clasificación, filtros, usuarios, relaciones y bloqueos utilizan ahora un Admin persistido propio por caso. TicketEndpointsTests.Authentication.cs emite JWT reales con JwtTokenIssuer y usa el handler Bearer y políticas de producción, sin esquema de prueba, cabecera de bypass ni excepciones en la API. Las fábricas derivadas reciben tokens de su propio emisor/configuración. Registro/login siguen probándose sin token. Todos los usuarios y tickets propios se registran para limpieza: primero tickets, después usuarios; no hay borrados globales ni reinicio de secuencias.

Se añaden 42 casos PostgreSQL: visibilidad por rol e históricos, totales/paginación sin filtraciones, creador obtenido de identidad pese a parámetros falsificados, denegación de gestión para User/Technician, estado solo para técnico asignado, perfil privado, rol modificado/cuenta eliminada/claim privilegiado sin respaldo en base, administración sin alterar hashes/ID/fecha, validación y duplicados concurrentes, ocho combinaciones de cambio de rol/asignación (ambos órdenes, Technician/Admin y asignación/reasignación), y espera/rechazo de estado al reasignar concurrentemente. Los tests de asignaciones compartidas y borrado restringido anteriores se mantienen. La sincronización usa interceptores y pg_blocking_pids; los límites temporales solo detectan fallos.

Los 61 casos nuevos sin PostgreSQL prueban las 13 rutas protegidas con token ausente/inválido/caducado/manipulado, la lista exacta de rutas públicas y política fallback, e identidades firmadas inválidas/ambiguas. El lector JWT rechaza un sub duplicado con 401; otros claims inválidos que llegan a la política se rechazan con 403 antes de resolver DbContext. Las seis pruebas anteriores de gestión anónima pasan ahora a exigir 401 en todos los entornos, conservando el rechazo antes de enlazar el cuerpo o acceder a base.

Resultado final: 216 sin PostgreSQL ejecutadas y superadas, cero fallidas u omitidas. 273 PostgreSQL compiladas y descubiertas sin ejecución. HELPDESK_TEST_CONNECTION_STRING ausente: no se abrió ninguna conexión, se aplicaron migraciones ni se comprobó la concurrencia real en esta sesión. Las nuevas garantías PostgreSQL quedan pendientes de ejecución segura; el descubrimiento no demuestra que pasen. Fixture y PostgreSqlTestSettings no cambian y siguen exigiendo exclusivamente helpdesk_test_db, rol limitado y conexión explícita.

Para repetir de forma aislada sin imprimir secretos, seguir la carga oculta de la variable al inicio de esta guía y ejecutar:

```bash
dotnet test HelpDesk.NET.slnx --filter 'Category=PostgreSQL'
```

No usar DefaultConnection de desarrollo ni cambiar usuarios existentes para obtener Admin. Las cuentas de roles de pruebas se preparan por EF solo dentro de la base validada; esto no proporciona una vía de aprovisionamiento en la API. La comprobación manual de solo lectura para cuentas ya activadas está en AUTHENTICATION.md.

## Bootstrap CLI del primer Admin — etapa 9.4

Suite al implementar inicialmente 9.4 (registro histórico): **537 casos**, **257 sin PostgreSQL superados** y **280 PostgreSQL descubiertos sin ejecutar**. El cierre posterior confirma 538 pruebas superadas, incluidas las 281 PostgreSQL de la suite corregida. Se añaden 41 casos sin PostgreSQL y siete de integración. Los resultados de 9.3 anteriores son históricos. Esta sesión no tiene HELPDESK_TEST_CONNECTION_STRING: no se ejecutó bootstrap sobre una base ni se aplicaron migraciones.

Las pruebas sin base verifican validación compartida, hash, confirmación exacta, destino, rechazo de argumentos/redirección y ausencia de secretos en errores. Una ejecuta la consola real en pseudoterminal Linux y cancela antes de conectar. Requiere `script` (util-linux), `stty` (coreutils) y `dotnet` en PATH; la solución debe compilarse previamente. La PTY registra solo en memoria y usa /dev/null como destino de script, nunca un archivo de transcripción. No hay flags ni variables para saltarse el requisito interactivo. Las credenciales de ese caso son efímeras ficticias y nunca se usan para conectar.

Los siete casos PostgreSQL residen en TicketEndpointsTests.Bootstrap.cs, dentro de la misma clase parcial que las regresiones: se ejecutan secuencialmente con la fixture existente y solo aceptan la conexión previamente validada a helpdesk_test_db. No leen la variable de bootstrap del operador ni DefaultConnection. Antes de empezar exigen ausencia de Admin ajenos; **no borran, degradan ni cambian hashes** para conseguirla. Si hay un Admin persistido por otra ejecución, revisar su procedencia y usar una base de pruebas dedicada al proyecto que cumpla las precondiciones, sin modificar desarrollo ni ejecutar borrados globales.

Se preparan dos usuarios y seis tickets propios, con roles, hash anterior, relaciones y clasificaciones variadas. Tras cada operación se comparan los campos y hashes sin imprimirlos. Se prueba creación con normalización, PasswordHasher, login HTTP, claim Admin, GET administrativo y creación de otra cuenta propia mediante la API. También Admin existente con/sin hash, correo User/Technician duplicado y cancelación. Limpieza heredada limitada a GUID/IDs propios, primero tickets y después usuarios; un aborto o pérdida de conexión puede impedirla.

El caso concurrente inicia **dos procesos reales** de HelpDesk.AdminBootstrap bajo PTY, con la conexión de pruebas por entorno y contraseña por entrada oculta. Una transacción del test mantiene inicialmente el bloqueo de Users; pg_locks confirma que ambos procesos esperan ShareRowExclusiveLock antes de liberarlo. Se exige un código de creación 0, un rechazo 2, exactamente una fila Admin candidata, hash correcto y conservación de la línea base. Se capturan stdout/stderr en memoria y se comprueba que no contienen contraseñas, hash ni cadena; no se vuelcan esas capturas ante fallos. Los timeouts detectan errores, no coordinan el orden. Este caso está compilado/descubierto, **no ejecutado** en esta sesión.

Comandos desde la raíz:

```bash
dotnet restore HelpDesk.NET.slnx
dotnet build HelpDesk.NET.slnx --no-restore
dotnet test HelpDesk.NET.slnx --no-build --filter 'Category!=PostgreSQL'
```

Solo con la conexión segura cargada de forma oculta según esta guía:

```bash
dotnet test HelpDesk.NET.slnx --no-build --filter 'Category=PostgreSQL'
```

Para ejecutar únicamente el nuevo bootstrap PostgreSQL sin sustituir la validación de destino:

```bash
dotnet test HelpDesk.NET.slnx --no-build --filter 'Category=PostgreSQL&FullyQualifiedName~Bootstrap'
```

Listar con --list-tests no ejecuta fixture ni valida persistencia/concurrencia. Los ejemplos manuales que sí crean el primer Admin real están en AUTHENTICATION.md y requieren autorización específica para el destino; el primer Admin de desarrollo ya fue creado y verificado y no debe recrearse.
