# Autenticación y autorización — etapas 9.1–9.4

Estado actual: registro, login JWT, autorización por roles/recursos y CLI separada del primer Admin implementados. Bootstrap real pendiente de autorización; sin recuperación, correos, frontend ni activación de cuentas anteriores. No se han aplicado migraciones ni modificado datos de desarrollo. Los apartados de transición 9.1 y los límites iniciales 9.2 son históricos; los permisos vigentes están en la sección 9.3 al final.

## Hash y salt

Un hash de contraseña es una derivación costosa, verificable pero no descifrable. Una filtración de texto plano permitiría usar inmediatamente las contraseñas; un hash obliga a intentar adivinarlas. El salt aleatorio hace que contraseñas iguales tengan hashes distintos y evita tablas precalculadas compartidas. No es un secreto y se almacena dentro del formato del hash junto a sus parámetros.

Se registra `IPasswordHasher<User>` → `PasswordHasher<User>` por DI, usando el framework compartido de ASP.NET Core; no hay nuevos paquetes ni tablas de Identity. Formato V3, PBKDF2-HMAC-SHA512 y 210.000 iteraciones, salt generado por la implementación estándar. El formato permite verificar y reconocer hashes que necesiten actualización de parámetros en una etapa futura. Referencias: [implementación oficial de PasswordHasher](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/PasswordHasher.cs), [PasswordHasherOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.passwordhasheroptions.iterationcount), [recomendación PBKDF2 de OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2).

## Registro

`POST /api/auth/register`, Content-Type application/json, recibe tres strings: `name`, `email`, `password`. El DTO no contiene Role ni PasswordHash. Los campos adicionales se ignoran siguiendo el comportamiento JSON existente: nunca modifican el rol, hash, ID ni fecha generados por el servidor.

- Nombre/correo: se llama a UserValidation con Role fijo User; se mantienen las reglas y normalización existentes, CHECK e índice único de Email.
- Contraseña: obligatoria, 15–128 unidades UTF-16 (`string.Length`), no solo espacios ni caracteres de control. Permite Unicode y espacios normales sin recortar, normalizar ni truncar; no exige mayúsculas, números o símbolos. Se favorecen frases largas; esta política no incluye todavía comprobación de contraseñas filtradas. [Criterios de longitud y composición de OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html#implement-proper-password-strength-controls).
- Rol siempre User. CreatedAt UTC e Id generado por PostgreSQL. Se deriva el hash antes de añadir la entidad; nunca se asigna la contraseña a un modelo persistido.
- Éxito: 201, Location `/api/users/{id}` y UserResponseDto (`id`, `name`, `email`, `role`, `createdAt`). Sin token ni sesión.
- Entrada inválida/JSON incorrecto: 400 y `error` fijo, sin reflejar contraseñas.
- Correo duplicado: 409 y el mensaje existente. AnyAsync anticipa duplicados; el índice IX_Users_Email y la captura exclusiva de 23505 de ese índice cubren carreras. Una cuenta sin contraseña tampoco puede reclamarse repitiendo el registro.

PasswordHash tiene JsonIgnore como defensa adicional; los GET siguen proyectando DTOs públicos. No se añade logging de cuerpos, contraseñas o hashes ni se habilita EnableSensitiveDataLogging. Mantener estos controles también en el proxy/hosting; para uso fuera de local transportar credenciales solo mediante HTTPS. Los tests generan contraseñas válidas efímeras y no imprimen sus valores ni hashes.

## Transición histórica 9.1: bloqueo de gestión pública de usuarios

POST `/api/users` y PUT `/api/users/{id}` devuelven siempre 403 con JSON error antes de leer cuerpos, resolver DbContext o consultar el ID. El bloqueo es incondicional en todos los entornos, sin configuración de excepción ni secretos fijos. Se eliminan los handlers de escritura públicos anteriores. GET de usuarios y endpoints de tickets mantienen su comportamiento.

El desarrollo no puede crear técnicos/administradores ni editar usuarios por HTTP mientras dure esta transición. No se añade un bootstrap ni una herramienta alternativa de privilegios. Los usuarios existentes conservan sus roles y los tests preparan filas propias mediante EF en la base validada de pruebas.

Las pruebas de éxito/error del antiguo POST/PUT y las carreras que requerían un cambio de rol público se sustituyen por pruebas de 403 y conservación de datos. No se ocultan con skip ni se introduce un bypass de Testing. Se conservan los tests de modelos, validación compartida, persistencia, GET, tickets y las carreras de asignaciones compatibles/borrado. El registro conserva y prueba la carrera de unicidad mediante una barrera después del chequeo previo.

Al reintroducir PUT con autorización real debe mantenerse el protocolo de la etapa 8.3: transacción ReadCommitted, bloqueo FOR NO KEY UPDATE al leer el usuario, consulta posterior de asignaciones y commit; assignment mantiene FOR SHARE hasta commit. Nunca reactivar simplemente la ruta antigua sin autorización.

## Cuentas anteriores

La columna PasswordHash es nullable deliberadamente. NULL significa que no hay credencial válida, no contraseña vacía ni permiso de acceso. No hay login todavía; cuando se implemente deberá rechazar antes de verificar cualquier cuenta con hash null/vacío, sin fallback a correo, contraseña vacía u otra credencial predeterminada.

En una etapa futura, esas cuentas podrán establecer su primera contraseña solo tras verificar la titularidad mediante un proceso de activación seguro: token aleatorio, de uso único, con caducidad, vinculado a la cuenta y almacenado de forma protegida; entrega por un canal de titularidad verificado. Se debe proteger contra enumeración y abuso e invalidar el token al consumirlo. Para cuentas privilegiadas se requiere además control administrativo autenticado. Nada de ese flujo se implementa ahora. No aceptar el ID/correo como prueba de titularidad, asignar contraseñas comunes ni enviar contraseñas por correo.

## Migración y revisión previa a desarrollo

`20261021123515_AddUserPasswordHash.Up` contiene solo AddColumn<string>, Users.PasswordHash text nullable, sin default ni backfill. SQL incremental revisado:

```sql
ALTER TABLE "Users" ADD "PasswordHash" text;
```

El script incluye la transacción y el registro normal en __EFMigrationsHistory. No toca Tickets, FK, índices ni filas anteriores; las cuentas reciben NULL. El DDL requiere un bloqueo de tabla breve. Down elimina solo la columna: perdería los hashes, por lo que revertir requiere evaluar esa pérdida, aunque conserve los demás datos.

Antes de aplicar a desarrollo, seguir estos pasos manuales:

1. Revisar el diff de la migración, Designer y snapshot; confirmar que únicamente se añade PasswordHash. Modelo y snapshot se pueden comparar sin conexión.
2. Generar el script sin credenciales ni acceso a desarrollo, con configuración temporal de diseño:

```bash
dotnet ef migrations script AddTicketUserRelations AddUserPasswordHash --project HelpDesk.Api --startup-project HelpDesk.Api --context HelpDeskDbContext --output /tmp/helpdesk-password-hash-review.sql -- --environment Testing --ConnectionStrings:DefaultConnection 'Host=localhost;Database=helpdesk_test_db'
```

3. Preparar HELPDESK_TEST_CONNECTION_STRING fuera del repositorio siguiendo POSTGRESQL_TESTS.md; comprobar base helpdesk_test_db y rol helpdesk_test_user. Ejecutar restore/build y ambas suites por separado. La fixture valida destino/permisos antes de aplicar migraciones exclusivamente a pruebas. Sin conexión segura no ejecutar PostgreSQL ni usar DefaultConnection como alternativa.
4. Revisar las pruebas de registro, duplicados concurrentes, 403, no exposición de hashes y preservación. La prueba de migración ejecuta el DDL generado sobre tablas temporales anteriores con filas y FK; no revierte el esquema compartido ni toca desarrollo.
5. Antes de cualquier aplicación manual a desarrollo: respaldo, historial hasta AddTicketUserRelations confirmado, ausencia de columna creada fuera de EF y parada coordinada de la API. Aplicar solo después de la revisión del usuario; esta sesión no ejecuta database update. Comparar usuarios/tickets/relaciones antes y después, confirmar PasswordHash NULL en cuentas anteriores y que los nuevos registros guardan un hash verificable. No mostrar hashes en consultas o capturas compartidas.

La API actualizada necesita la migración revisada antes de usar entidades User contra un esquema anterior. El estado de pruebas realmente ejecutadas figura en PROGRESS.md; descubrir casos no valida su comportamiento PostgreSQL.

## Archivos de esta etapa

| Estado | Archivo | Propósito |
| --- | --- | --- |
| Nuevo | `HelpDesk.Api/DTOs/RegisterUserDto.cs` | Entrada limitada a nombre, correo y contraseña |
| Nuevo | `HelpDesk.Api/Validation/PasswordValidation.cs` | Política de contraseña compartida y comprobable |
| Nuevo | `HelpDesk.Api/Migrations/20261021123515_AddUserPasswordHash.cs` | Añadir columna nullable |
| Nuevo | `HelpDesk.Api/Migrations/20261021123515_AddUserPasswordHash.Designer.cs` | Metadatos generados de la migración |
| Nuevo | `HelpDesk.Tests/RegistrationSecurityTests.cs` | Hashing, política, privacidad y 403 sin base |
| Nuevo | `HelpDesk.Tests/UserPasswordMigrationTests.cs` | Operaciones acotadas de la migración |
| Nuevo | `HelpDesk.Tests/Integration/TicketEndpointsTests.Registration.cs` | Registro, concurrencia y conservación |
| Nuevo | `HelpDesk.Tests/Integration/TicketEndpointsTests.PasswordMigration.cs` | DDL sobre filas y relaciones anteriores temporales |
| Nuevo | `docs/AUTHENTICATION.md` | Diseño, transición y revisión previa |
| Modificado | `HelpDesk.Api/Models/User.cs` | Hash nullable excluido de JSON |
| Modificado | `HelpDesk.Api/Data/HelpDeskDbContext.cs` | Mapeo text NULL |
| Modificado | `HelpDesk.Api/Program.cs` | DI del hasher, registro y retirada de escrituras públicas |
| Modificado | `HelpDesk.Api/Migrations/HelpDeskDbContextModelSnapshot.cs` | Snapshot actualizado |
| Modificado | `HelpDesk.Tests/Integration/TicketEndpointsTests.UserHttp.cs` | Conservar GET/helpers y retirar contrato de escritura anterior |
| Modificado | `HelpDesk.Tests/Integration/TicketEndpointsTests.Relations.cs` | Retirar cambios de rol HTTP ahora prohibidos |
| Modificado | `HelpDesk.Tests/Integration/TicketEndpointsTests.AssignmentConcurrency.cs` | Conservar carreras de assignment/borrado, retirar carreras del PUT anterior |
| Modificado | `README.md` | Contratos y estado actual de pruebas |
| Modificado | `docs/ARCHITECTURE.md` | Decisión 035 y evolución del protocolo anterior |
| Modificado | `docs/PROGRESS.md` | Estado real y verificaciones ejecutadas |
| Modificado | `docs/POSTGRESQL_TESTS.md` | Suite actual, aislamiento y validación pendiente |

## Etapa 9.2: login y JWT

Estado al cerrar 9.2 (histórico): login y JWT implementados, aún sin autorización por roles. Los apartados 9.1 anteriores conservan el registro histórico; las cuentas con hash NULL ya se rechazan al iniciar sesión. No hay migración nueva.

`POST /api/auth/login` recibe `email` y `password` como strings. Normaliza únicamente el correo con `Trim().ToLowerInvariant()`, consulta PostgreSQL con `AsNoTracking` y usa `IPasswordHasher<User>.VerifyHashedPassword`. Campos ausentes, null o solo espacios producen 400. Cuenta inexistente, contraseña incorrecta y hash NULL/vacío o Base64 corrupto producen el mismo 401: `{"error":"Credenciales incorrectas."}`. No transforma la contraseña ni permite seleccionar rol. `SuccessRehashNeeded` se acepta sin actualizar el hash; una futura evolución deberá decidir su actualización segura.

Éxito: 200, `Cache-Control: no-store`, `Pragma: no-cache` y únicamente:

```json
{"accessToken":"<JWT_NO_REAL>","tokenType":"Bearer","expiresIn":1800}
```

`JwtTokenIssuer` usa `TimeProvider` para obtener UTC y emitir HS256. Incluye `sub` (ID), `email` canónico, `role` leído de la cuenta, `iss`, `aud`, `iat`, `exp` y `nbf`. La duración es 1.800 segundos. JWT está firmado, no cifrado: sus claims son legibles; no contiene contraseñas ni hashes.

`AddAuthentication().AddJwtBearer()` registra el handler estándar. `UseAuthentication()` prepara el principal. La etapa 9.3 añade a continuación `UseAuthorization()`, políticas y comprobación de cuenta actual descritas al final. El handler valida firma HS256, emisor, audiencia, presencia de caducidad y vigencia. La tolerancia temporal es de **30 segundos**: un token puede aceptarse hasta ese margen después de expirar; sincronizar relojes. Se desactiva el mapeo de claims, el almacenamiento del token en propiedades de autenticación y los detalles de error Bearer.

### Configuración en Linux Mint

Desde la raíz del repositorio, conservar la conexión local existente fuera de Git. Configurar obligatoriamente `Jwt:Issuer`, `Jwt:Audience` y `Jwt:SigningKeyBase64`. También se admiten variables `Jwt__Issuer`, `Jwt__Audience` y `Jwt__SigningKeyBase64` suministradas mediante un gestor de secretos. No hay valores predeterminados ni claves automáticas de arranque. `ValidateOnStart` impide arrancar incluso health si falta configuración o es inválida, con mensajes que no incorporan valores.

Issuer y audience deben ser strings no vacíos sin espacios extremos; no necesitan ser URLs. La clave debe ser Base64 de al menos 32 bytes aleatorios criptográficos (256 bits). Se rechazan patrones con menos de 16 bytes distintos como defensa contra errores triviales; ninguna comprobación de longitud o diversidad demuestra entropía. Generarla con un CSPRNG es obligatorio.

Este comando genera **una sola vez** 64 bytes aleatorios y los guarda por stdin en User Secrets, sin imprimir la clave ni pasarla por argumentos/historial y sin crear archivos en el repositorio. Ejecutarlo de nuevo cambia la clave e invalida tokens anteriores; no ejecutarlo en cada arranque. Los identificadores de emisor y audiencia son públicos.

```bash
python3 - <<'PY'
import base64
import json
import secrets
import subprocess

settings = {
    'Jwt:Issuer': 'HelpDesk.Local',
    'Jwt:Audience': 'HelpDesk.Local.Client',
    'Jwt:SigningKeyBase64': base64.b64encode(secrets.token_bytes(64)).decode('ascii'),
}
result = subprocess.run(
    ['dotnet', 'user-secrets', 'set', '--project', 'HelpDesk.Api/HelpDesk.Api.csproj'],
    input=json.dumps(settings), text=True, capture_output=True,
)
if result.returncode:
    raise SystemExit('No se pudo guardar la configuración JWT.')
print('Configuración JWT guardada fuera del repositorio.')
PY
```

User Secrets es almacenamiento local sin cifrar: proteger la cuenta y sus archivos; no compartir `dotnet user-secrets list`. Para despliegue usar un gestor de secretos y HTTPS; la clave HS256 debe ser compartida solo por componentes de confianza que puedan emitir tokens. No guardarla en appsettings, logs ni capturas.

### Probar el login sin escribir datos de desarrollo

1. Configurar JWT como arriba y conservar la conexión de desarrollo ya preparada. El esquema debe incluir AddUserPasswordHash; esta etapa no requiere aplicar migraciones. Usar una cuenta registrada anteriormente con contraseña conocida. Una cuenta antigua sin hash devolverá 401; no crear ni modificar cuentas de desarrollo para esta comprobación.
2. Arrancar en otra terminal:

```bash
dotnet run --project HelpDesk.Api --launch-profile http
```

3. Ejecutar desde Linux Mint el siguiente cliente. Solicita contraseña oculta, envía JSON por HTTP local y comprueba el contrato sin imprimir ni guardar el token. No añade cabeceras Bearer a endpoints públicos ni usa servicios externos para decodificarlo.

```bash
python3 - <<'PY'
import getpass
import json
import urllib.error
import urllib.request

email = input('Email de una cuenta registrada: ')
password = getpass.getpass('Contraseña: ')
request = urllib.request.Request(
    'http://localhost:5085/api/auth/login',
    data=json.dumps({'email': email, 'password': password}).encode('utf-8'),
    headers={'Content-Type': 'application/json'}, method='POST',
)
try:
    with urllib.request.urlopen(request) as response:
        body = json.load(response)
        assert set(body) == {'accessToken', 'tokenType', 'expiresIn'}
        assert body['tokenType'] == 'Bearer' and body['expiresIn'] == 1800
        assert isinstance(body['accessToken'], str) and len(body['accessToken'].split('.')) == 3
        print('HTTP 200; token recibido sin mostrarlo; duración: 1800 segundos.')
except urllib.error.HTTPError as error:
    print('HTTP', error.code, '; revisar credenciales o configuración sin compartir secretos.')
PY
```

4. Repetir con espacios/mayúsculas en el correo: debe devolver 200. Contraseña incorrecta o correo inexistente: 401 genérico. Contraseña vacía: 400. Detener la API con Ctrl+C. Esta comprobación solo lee datos; no verifica por sí sola la firma criptográfica.
5. Para las comprobaciones automáticas de firma y errores, ejecutar `dotnet test HelpDesk.NET.slnx --filter 'Category!=PostgreSQL'`. Las pruebas HTTP completas de login usan exclusivamente la fixture y conexión explícita a `helpdesk_test_db` descritas en POSTGRESQL_TESTS.md; no sustituyen esa variable por la conexión de desarrollo.

### Límites históricos al cerrar 9.2 (evolucionados en 9.3)

- Un JWT emitido no se revoca automáticamente al cambiar el rol, contraseña o deshabilitar una cuenta. El modelo actual aún no incorpora deshabilitación. La estrategia de revocación y comprobación de estado corresponde al diseño futuro de autorización.
- Los endpoints de tickets y los GET de usuarios conservan acceso público; un Bearer inválido no los restringe. POST/PUT de usuarios siguen devolviendo 403 incluso con JWT válido. No hay endpoint nuevo de perfil privado.
- No hay refresh tokens, cierre de sesión con revocación, recuperación, frontend ni activación de cuentas anteriores. Etapa 9.3 pendiente de revisión y solicitud.
- El error de credenciales es uniforme, pero no se igualan tiempos entre cuenta inexistente y verificación PBKDF2. No se añaden todavía limitación de intentos ni bloqueo: evaluar abuso y enumeración temporal antes de exponer el servicio.
- No registrar cuerpos, contraseñas, hashes ni cabeceras Authorization en aplicación, proxy o herramientas de observabilidad. El manejador global responde 500 genérico también en Development para evitar reflejar headers sensibles en la página de excepciones; conserva el diagnóstico interno estándar, sin habilitar logging sensible de EF.

## Etapa 9.3: permisos vigentes

`AddAuthorization` configura políticas estándar y `UseAuthorization` se ejecuta después de `UseAuthentication`. Solo GET health y POST register/login llevan `AllowAnonymous`. Todas las otras rutas llevan `RequireAuthorization`; una política fallback protege por defecto futuras rutas sin metadatos explícitos.

| Método y ruta | Permiso |
| --- | --- |
| GET `/api/health` | Público |
| POST `/api/auth/register` | Público, crea exclusivamente User |
| POST `/api/auth/login` | Público, verifica credenciales y emite JWT |
| GET `/api/tickets` | User: propios; Technician: propios o asignados; Admin: todos |
| GET `/api/tickets/{id}` | Misma visibilidad que el listado |
| POST `/api/tickets` | Cualquier cuenta autorizada; siempre creador propio |
| PUT `/api/tickets/{id}` | Solo Admin |
| PATCH `/api/tickets/{id}/status` | Admin o Technician asignado al ticket |
| PATCH `/api/tickets/{id}/priority` | Solo Admin |
| PATCH `/api/tickets/{id}/category` | Solo Admin |
| PATCH `/api/tickets/{id}/assignment` | Solo Admin |
| DELETE `/api/tickets/{id}` | Solo Admin |
| GET `/api/users` | Solo Admin |
| GET `/api/users/{id}` | Admin o la propia cuenta |
| POST `/api/users` | Solo Admin; crea sin credencial |
| PUT `/api/users/{id}` | Solo Admin; edita nombre, correo y rol |

### Identidad y rol actual

`CurrentAccountHandler` exige JWT autenticado, exactamente un `sub` entero positivo y exactamente un `role` reconocido. Consulta ID/rol de la cuenta en PostgreSQL, sin cargar el hash, y exige que el rol coincida con el claim. Completa `CurrentAccount` scoped únicamente tras comprobarlo; ningún ID ni rol se obtiene del cuerpo o parámetros del cliente.

La comprobación se hace en **cada petición protegida**, también en lecturas; no se mantiene una caché de roles entre peticiones. Cuenta eliminada o rol distinto: 403 genérico, sin datos ni explicación de qué condición falló. Incluso una promoción exige iniciar sesión otra vez. Una petición con token criptográficamente inválido, caducado o ausente produce 401 y challenge Bearer antes de leer el cuerpo o resolver el contexto. Un fallo de PostgreSQL produce 500 genérico: nunca se concede acceso usando solo claims como alternativa. Health/register/login permanecen públicos aunque se envíe un Bearer inválido.

El rol se comprueba al autorizar la petición, no continuamente durante su ejecución. Una petición que ya superó esa comprobación puede terminar después de una modificación concurrente del rol. No se ofrece revocación instantánea de peticiones en curso. Cambios de contraseña sin cambio de rol no invalidan tokens existentes. No hay deshabilitación de cuentas en el modelo actual, jti/denylist, refresh tokens ni logout con revocación. Un rol que cambia y luego vuelve al anterior puede hacer utilizable otra vez un JWT no caducado; una revocación permanente requerirá versión de sesión u otro diseño posterior.

### Acceso a recursos e históricos

`TicketAccess.VisibleTo` añade el predicado de propietario/asignado al IQueryable antes de filtros, conteo y paginación, y se usa también en el detalle. Un ticket ajeno y un ID inexistente devuelven el mismo 404 sin cuerpo. No se devuelven recuentos globales a User/Technician.

Los tickets históricos con creador NULL no se reasignan ni se alteran: Admin puede gestionarlos y un Technician puede leerlos/cambiar su estado únicamente si está asignado. Una asignación a User no le concede visibilidad. Un técnico puede consultar un ticket propio no asignado pero no cambiar su estado (403); si ni siquiera es visible, el PATCH de estado devuelve 404. Las operaciones vetadas por la política del rol devuelven 403 sin consultar el ticket, exista o no.

POST ignora `CreatedByUserId` enviado por cualquier rol, sea propio, ajeno, inexistente o NULL, y fija el creador a la cuenta autorizada. También ignora campos extra de asignación/rol; AssignedToUserId comienza NULL. Tipos JSON incompatibles siguen siendo 400 por el enlace del DTO. Las FK y la captura acotada de conflictos de integridad permanecen. La política de contraseñas y el registro no cambian.

PATCH de estado abre ReadCommitted y lee el ticket con FOR UPDATE antes de comprobar visibilidad/asignación. Conserva el bloqueo hasta guardar/commit o disponer la transacción, también en retornos tempranos. Así no se autoriza un cambio basándose en una asignación anterior a una reasignación concurrente. Se conservan transiciones, 409 e idempotencia. El resto de gestión de tickets requiere Admin y mantiene sus validaciones.

### Administración y bloqueo de activación

POST de usuarios reutiliza `CreateUserDto` (Name/Email/Role), validación existente e índice único; devuelve 201/Location/DTO sin hash. Crea PasswordHash NULL, sin contraseña por defecto ni token. Un campo extra password/passwordHash no crea credenciales. El registro público nunca reclama esas cuentas.

PUT reutiliza `UpdateUserDto` y solo cambia Name/Email/Role. Conserva ID, fecha y hash. Requiere transacción ReadCommitted, lectura del usuario con FOR NO KEY UPDATE, comprobación posterior de asignaciones y commit. Cambiar a User con tickets asignados produce 409 sin cambios. Assignment conserva FOR SHARE sobre el destino hasta commit: un cambio de rol y una asignación se serializan; quien espere vuelve a validar. Se mantienen normalización, validación de rol y correo, captura exclusiva de duplicados 23505 del índice de correo y conflictos de FK de tickets.

Al cerrar 9.3 no había un método para crear la **primera cuenta Admin con credencial válida** o activar una antigua sin hash. La CLI de 9.4 resuelve solo la creación de una cuenta nueva; el resto de este párrafo registra la limitación y propuesta anteriores. Si no existe un Admin que pueda iniciar sesión, la administración HTTP queda inaccesible deliberadamente. No editar cuentas existentes ni firmar tokens manualmente para eludirlo. Propuesta para una etapa separada: diseñar una herramienta de aprovisionamiento inicial de ejecución controlada, con verificación administrativa, auditoría y activación de un uso con caducidad. Requiere aprobación y diseño propios; no se implementa ahora. Tampoco se impide en esta etapa que el último Admin se degrade a sí mismo, lo que podría dejar la administración inaccesible.

### Prueba manual de cada rol en Linux Mint sin escribir datos

1. Conservar la conexión y clave JWT locales ya configuradas. No volver a generar la clave en cada arranque ni aplicar migraciones. No se necesita ningún cambio de esquema en 9.3.
2. Arrancar desde la raíz en otra terminal:

```bash
dotnet run --project HelpDesk.Api --launch-profile http
```

3. Ejecutar el siguiente cliente de solo lectura para User, Technician y Admin. Introducir únicamente cuentas existentes con contraseña conocida; dejar vacío el email si no existe una cuenta activada para ese rol. Ni la contraseña ni el token se muestran, guardan en archivos o pasan por argumentos. No copiar el JWT a decodificadores externos.

```bash
python3 - <<'PY'
import base64
import getpass
import json
import urllib.error
import urllib.request

base = 'http://localhost:5085'

def call(method, path, token=None, body=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    request = urllib.request.Request(base + path,
        data=None if body is None else json.dumps(body).encode('utf-8'),
        headers=headers, method=method)
    try:
        with urllib.request.urlopen(request) as response:
            data = response.read()
            return response.status, json.loads(data) if data else None
    except urllib.error.HTTPError as error:
        return error.code, None
    except urllib.error.URLError:
        raise SystemExit('No se pudo conectar a la API local.')

print('Health sin token:', call('GET', '/api/health')[0])
print('Tickets sin token:', call('GET', '/api/tickets')[0])
print('Tickets con token inválido:', call('GET', '/api/tickets', token='invalid-token')[0])
for role in ('User', 'Technician', 'Admin'):
    email = input('Email existente para ' + role + ' (vacío para saltar): ').strip()
    if not email:
        print('Sin cuenta activada: prueba manual de este rol pendiente.')
        continue
    status, login = call('POST', '/api/auth/login', body={
        'email': email, 'password': getpass.getpass('Contraseña: ')})
    print('Login:', status)
    if status != 200:
        continue
    token = login['accessToken']
    # Extraer el ID de la respuesta del propio login solo para elegir la URL de perfil.
    # Esto no valida la firma: el servidor valida y autoriza cada petición siguiente.
    payload = token.split('.')[1]
    claims = json.loads(base64.urlsafe_b64decode(payload + '=' * (-len(payload) % 4)))
    if claims.get('role') != role:
        print('La cuenta no tiene el rol elegido; usar la cuenta adecuada sin cambiar sus datos.')
        continue
    print('Perfil propio:', call('GET', '/api/users/' + claims['sub'], token)[0])
    print('Directorio de usuarios:', call('GET', '/api/users', token)[0])
    status, page = call('GET', '/api/tickets?page=1&pageSize=1', token)
    print('Listado de tickets:', status)
    if page is not None:
        print('Total visible:', page['totalCount'])
    for label in ('propio', 'ajeno', 'histórico', 'asignado'):
        ticket_id = input('ID conocido de ticket ' + label + ' (vacío para saltar): ').strip()
        if ticket_id.isascii() and ticket_id.isdecimal():
            print('Detalle ' + label + ':', call('GET', '/api/tickets/' + ticket_id, token)[0])
    # No se imprimen ni persisten token, claims completos o respuestas privadas.
PY
```

4. Resultados: health 200; tickets sin token/inválido 401. Cada perfil propio 200. Directorio: User/Technician 403, Admin 200. Ticket propio: 200; ajeno no asignado: User/Technician 404, Admin 200. Histórico sin asignar: User/Technician 404, Admin 200. Histórico asignado al técnico: ese Technician 200. Los totales solo incluyen tickets visibles; conocer un ID no cambia permisos. Detener la API con Ctrl+C.
5. No probar PUT/DELETE/PATCH ni crear usuarios sobre datos existentes. Para verificar también escrituras, falsificación de creador, roles cambiados, creación por cada rol y concurrencia, usar exclusivamente la base aislada ya preparada según POSTGRESQL_TESTS.md:

```bash
read -r -s -p 'Cadena exclusiva de helpdesk_test_db: ' HELPDESK_TEST_CONNECTION_STRING
export HELPDESK_TEST_CONNECTION_STRING
dotnet test HelpDesk.NET.slnx --filter 'Category=PostgreSQL'
unset HELPDESK_TEST_CONNECTION_STRING
```

La fixture valida destino/permisos antes de migrar exclusivamente pruebas. Genera cuentas y tickets propios, utiliza JWT reales del emisor de la API y limpia solo sus filas. Las pruebas de login siguen verificando credenciales por HTTP; los helpers de regresión pueden emitir JWT firmados para sus usuarios persistidos sin instalar un esquema falso ni omitir políticas. Este comando no es una prueba manual contra desarrollo. Si no hay conexión segura, dejarlo pendiente: no usar la conexión de desarrollo como alternativa.

## Etapa 9.4: inicialización del primer Admin mediante CLI

Estado: herramienta implementada y separada de HTTP. **No se ha ejecutado contra desarrollo ni creado una cuenta real. Esa ejecución queda pendiente de autorización explícita del usuario.** Las referencias anteriores a bootstrap pendiente describen el estado de 9.3. No se activan cuentas antiguas: esta etapa solo permite crear una nueva.

`HelpDesk.AdminBootstrap` es una consola net10.0 que referencia la API para reutilizar modelos, contexto y validación. Cargar ese ensamblado no ejecuta el punto de entrada HTTP. La API no referencia la consola ni llama al bootstrap al arrancar, registrar, autenticar o migrar. No hay endpoint nuevo ni cambios de permisos.

API y CLI usan `AddHelpDeskPasswordHashing`: PasswordHasher<User> V3 y 210.000 iteraciones, con salt del framework. La entrada exige las mismas reglas de nombre/correo y contraseña de registro; el email se normaliza, la contraseña no se recorta ni transforma. Se pide dos veces sin eco y no se aceptan argumentos con credenciales. Escape, Ctrl+C/Ctrl+D durante la contraseña o confirmaciones incorrectas cancelan. Se rechaza stdin/stdout redirigidos; `--help` no abre conexiones.

### Configuración y confirmación del destino

La consola lee **únicamente** `HELPDESK_BOOTSTRAP_CONNECTION_STRING`. No usa DefaultConnection, appsettings, User Secrets, variables JWT ni la conexión de pruebas como alternativas. Que la API utilice User Secrets no autoriza a reutilizar su destino automáticamente. La consola consume y elimina su variable de su propio entorno; el operador debe eliminarla también del shell después de usarla.

La cadena requiere Host, Database, Username y Password explícitos. Admite Port, SSL Mode, Timeout, Command Timeout y Pooling; Include Error Detail, Persist Security Info y Enlist solo admiten false. No se admiten destinos múltiples, Options, Search Path u otras opciones ajenas a esta lista. Se fija el esquema public del modelo actual y no se crea ni migra el esquema. La cadena debe respetar el quoting de Npgsql para valores con punto y coma/comillas. No se imprime la cadena ni ninguna excepción del proveedor.

La CLI muestra host/puerto, base, usuario PostgreSQL y que creará un Admin nuevo sin modificar cuentas anteriores. Solicita que se escriba el nombre exacto de la base, comparado de forma sensible a mayúsculas. Antes de conectar/escribir exige `CONFIRMAR PRIMER ADMIN` sin espacios adicionales. Dentro de la transacción vuelve a comprobar `current_database()` y `current_user` frente al destino confirmado. No se acepta un nombre de base por convenio ni se presupone que helpdesk_db sea el destino correcto.

Usar una cuenta PostgreSQL legítima que pueda leer todos los usuarios, insertar el nuevo, utilizar la secuencia de ID y bloquear Users: ser propietario o disponer además de UPDATE, DELETE o TRUNCATE para este modo de bloqueo, según PostgreSQL 16. No hace falta un superusuario ni privilegios para crear bases. No usar políticas RLS/vistas que oculten administradores a esa cuenta. El operador que posee esas credenciales ya puede escribir en la base; esta CLI no constituye una barrera frente a un administrador PostgreSQL malicioso. Para destinos remotos proteger la conexión y verificar el certificado; no enviar credenciales por redes sin protección.

### Transacción y carrera entre inicializadores

El hash se calcula antes de tomar el bloqueo. A continuación, en ReadCommitted, la consola bloquea `public."Users"` con SHARE ROW EXCLUSIVE, comprueba si existe **cualquier** rol Admin, comprueba el correo y solo entonces inserta un usuario nuevo con su hash. Confirma una única transacción; retornos tempranos y errores liberan el bloqueo por rollback/disposición.

Ese modo es autoconflictivo y bloquea el ROW EXCLUSIVE de INSERT/UPDATE/DELETE, incluso de otro programa. Así el segundo inicializador espera y su consulta posterior, en ReadCommitted, ve el Admin confirmado por el primero y rechaza crear otro. Las lecturas normales pueden continuar. También se impide que una escritura concurrente cambie la condición mientras se realiza la inserción. Se toma el bloqueo más fuerte necesario antes de consultar/escribir y no se espera entrada humana dentro de él. [Bloqueos explícitos PostgreSQL 16](https://www.postgresql.org/docs/16/explicit-locking.html), [LOCK](https://www.postgresql.org/docs/16/sql-lock.html).

El bloqueo es de toda Users, deliberadamente breve y para una operación excepcional; coordinar una ventana tranquila porque puede esperar o retrasar escrituras. No bloquea intencionadamente Tickets ni modifica sus filas. No añade un indicador persistente ni una migración. La condición es ausencia actual de Admin: si en el futuro desaparecen/degradan todos, el operador con acceso legítimo a PostgreSQL podría volver a crear uno. No usar esto como procedimiento de recuperación.

Si existe Admin, incluso con hash NULL o contraseña perdida, el bootstrap se rechaza y no restablece nada. Si existe el correo, aunque pertenezca a User/Technician sin contraseña, se rechaza sin promoverlo ni activarlo. No hay contraseña por defecto, token maestro, emisión directa de JWT ni excepción por entorno.

### Ejecución manual en Linux Mint (pendiente de autorización)

Estos pasos escriben **una cuenta nueva** en el destino que confirme el operador. No ejecutarlos aún sobre helpdesk_db; la implementación deja la creación real pendiente de revisión/autorización. No necesitan cambiar los dos usuarios o seis tickets existentes, ni ejecutar migraciones.

1. Desde la raíz, restaurar y compilar. La base elegida debe existir y tener el esquema vigente, incluida PasswordHash; la herramienta falla si falta, sin repararlo automáticamente.

```bash
dotnet restore HelpDesk.NET.slnx
dotnet build HelpDesk.NET.slnx --no-restore
```

2. Revisar fuera de la CLI el destino y permisos con el responsable de PostgreSQL. Preparar respaldo y una ventana sin escrituras administrativas. No cambiar cuentas existentes para permitir el bootstrap. Cargar la cadena mediante entrada oculta, sin incluir su valor en el comando, historial o argumentos:

```bash
read -r -s -p 'Cadena PostgreSQL del destino revisado: ' HELPDESK_BOOTSTRAP_CONNECTION_STRING
printf '\n'
export HELPDESK_BOOTSTRAP_CONNECTION_STRING
dotnet run --project HelpDesk.AdminBootstrap --no-build --no-launch-profile
bootstrap_exit_code=$?
unset HELPDESK_BOOTSTRAP_CONNECTION_STRING
printf 'Código de salida: %s\n' "$bootstrap_exit_code"
```

No usar `echo` de la variable, `set -x`, grabación de terminal, `tee`, scripts con secretos ni servicios externos. El entorno del proceso no es un almacén cifrado y puede ser inspeccionado por procesos del mismo usuario o administradores del sistema. Cerrar la sesión o ejecutar `unset` también si se interrumpe la herramienta antes de terminar la secuencia.

3. Comprobar visualmente host, puerto, base y usuario PostgreSQL. Escribir el nombre exacto de la base. Introducir email **nuevo**, nombre y una contraseña elegida de 15–128 unidades UTF-16; repetir la misma contraseña sin eco. No se suministran ejemplos de contraseñas ni valores predeterminados.
4. Solo si el destino y la operación son correctos y están autorizados, escribir exactamente `CONFIRMAR PRIMER ADMIN`. Cualquier otra respuesta cancela. No hay cuenta creada hasta completar el commit. La CLI devuelve código 0 al crear; 2 por rechazo/cancelación/entrada inválida; 1 por fallo operativo. Los mensajes no incluyen hashes ni contraseñas.
5. Si aparece que ya existe Admin, detenerse y usar el login normal de ese Admin. La CLI no cambia su contraseña, no permite un segundo bootstrap y no acepta un correo existente como prueba de titularidad.

### Verificar login y acceso Admin sin modificar tickets

Una vez autorizado y ejecutado manualmente el bootstrap, iniciar la API con **su configuración JWT y conexión habituales**, apuntando al mismo destino revisado:

```bash
dotnet run --project HelpDesk.Api --launch-profile http
```

En otra terminal, este cliente pide la contraseña oculta, conserva el JWT solo en memoria y utiliza exclusivamente login y GET. Verifica que la API concede acceso al directorio reservado a Admin y devuelve el total de tickets visible sin mostrar datos privados ni el token:

```bash
python3 - <<'PY'
import getpass
import json
import urllib.error
import urllib.request

base = 'http://localhost:5085'
email = input('Email del Admin creado: ')
password = getpass.getpass('Contraseña: ')
request = urllib.request.Request(base + '/api/auth/login',
    data=json.dumps({'email': email, 'password': password}).encode('utf-8'),
    headers={'Content-Type': 'application/json'}, method='POST')
try:
    with urllib.request.urlopen(request) as response:
        login = json.load(response)
        if response.status != 200 or login.get('tokenType') != 'Bearer':
            raise SystemExit('El login no devolvió el contrato esperado.')
    token = login['accessToken']
    headers = {'Authorization': 'Bearer ' + token}
    with urllib.request.urlopen(urllib.request.Request(base + '/api/users', headers=headers)) as response:
        if response.status != 200:
            raise SystemExit('El directorio no está autorizado.')
        print('Login y acceso al directorio Admin: HTTP 200, sin mostrar token ni usuarios.')
    with urllib.request.urlopen(urllib.request.Request(base + '/api/tickets?pageSize=1', headers=headers)) as response:
        page = json.load(response)
        print('Listado Admin: HTTP', response.status, '; total visible:', page['totalCount'])
except urllib.error.HTTPError as error:
    print('HTTP', error.code, '; comprobar credenciales, rol actual y destino sin compartir secretos.')
except (urllib.error.URLError, ValueError, KeyError):
    raise SystemExit('No se pudo completar la comprobación local; no compartir salidas sensibles.')
PY
```

En la base descrita por el usuario se esperan seis tickets si no hubo otras escrituras y tres usuarios tras añadir el Admin a los dos anteriores. Esta sesión no comprueba esas cantidades en desarrollo. Un recuento por sí solo no acredita la conservación de cada campo; las pruebas aisladas comparan también roles, hashes, relaciones, estados, prioridades, categorías y demás campos anteriores. No ejecutar POST/PUT/PATCH/DELETE para verificar permisos sobre tickets reales.

### Fallos y límites operativos

- No hay recuperación de contraseña ni activación de cuentas anteriores. Si se pierde la contraseña, no borrar ni degradar al Admin para reabrir el bootstrap, ni editar hashes manualmente. Si hay otro Admin operativo, puede seguir administrando mediante las rutas existentes, pero tampoco hay un endpoint de recuperación. Diseñar y autorizar un flujo separado de recuperación con verificación de titularidad; preservar respaldos y datos mientras tanto.
- Ante fallo de conexión/permisos/esquema, corregir la configuración fuera del repositorio y volver a revisar el destino. No aumentar privilegios sin revisión ni usar otra base como alternativa. No se ejecutan migraciones automáticamente.
- Si se pierde la respuesta durante el commit, no se puede asegurar desde el cliente si se confirmó. Verificar mediante el login normal y revisión administrativa de solo lectura antes de reintentar; la condición de Admin evita duplicar el bootstrap tras un commit exitoso.
- Los strings de contraseña/conexión viven temporalmente en memoria administrada; no se promete borrado criptográfico de esa memoria. Proteger el equipo, volcados, entorno y terminal. La CLI no registra proveedores de logs ni imprime excepciones; su diagnóstico operativo es deliberadamente genérico.
- Las limitaciones de JWT, revocación y auto degradación del último Admin descritas en 9.3 no cambian. Este comando no crea un canal de administración HTTP sin autenticación.
