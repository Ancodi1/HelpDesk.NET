# Preparación de autenticación — etapa 9.1

Estado: registro y almacenamiento de hashes implementados; sin login, JWT, autorización, recuperación, correos ni frontend. No se han aplicado migraciones ni modificado datos de desarrollo.

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

## Gestión pública de usuarios

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

Estado actual: login y JWT implementados, sin autorización por roles ni restricciones nuevas. Los apartados 9.1 anteriores conservan el registro histórico; las cuentas con hash NULL ya se rechazan al iniciar sesión. No hay migración nueva.

`POST /api/auth/login` recibe `email` y `password` como strings. Normaliza únicamente el correo con `Trim().ToLowerInvariant()`, consulta PostgreSQL con `AsNoTracking` y usa `IPasswordHasher<User>.VerifyHashedPassword`. Campos ausentes, null o solo espacios producen 400. Cuenta inexistente, contraseña incorrecta y hash NULL/vacío o Base64 corrupto producen el mismo 401: `{"error":"Credenciales incorrectas."}`. No transforma la contraseña ni permite seleccionar rol. `SuccessRehashNeeded` se acepta sin actualizar el hash; una futura evolución deberá decidir su actualización segura.

Éxito: 200, `Cache-Control: no-store`, `Pragma: no-cache` y únicamente:

```json
{"accessToken":"<JWT_NO_REAL>","tokenType":"Bearer","expiresIn":1800}
```

`JwtTokenIssuer` usa `TimeProvider` para obtener UTC y emitir HS256. Incluye `sub` (ID), `email` canónico, `role` leído de la cuenta, `iss`, `aud`, `iat`, `exp` y `nbf`. La duración es 1.800 segundos. JWT está firmado, no cifrado: sus claims son legibles; no contiene contraseñas ni hashes.

`AddAuthentication().AddJwtBearer()` registra el handler estándar. `UseAuthentication()` prepara el principal; no se añade `RequireAuthorization`, políticas ni autorización por rol. El handler valida firma HS256, emisor, audiencia, presencia de caducidad y vigencia. La tolerancia temporal es de **30 segundos**: un token puede aceptarse hasta ese margen después de expirar; sincronizar relojes. Se desactiva el mapeo de claims, el almacenamiento del token en propiedades de autenticación y los detalles de error Bearer.

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

### Límites y decisiones pendientes

- Un JWT emitido no se revoca automáticamente al cambiar el rol, contraseña o deshabilitar una cuenta. El modelo actual aún no incorpora deshabilitación. La estrategia de revocación y comprobación de estado corresponde al diseño futuro de autorización.
- Los endpoints de tickets y los GET de usuarios conservan acceso público; un Bearer inválido no los restringe. POST/PUT de usuarios siguen devolviendo 403 incluso con JWT válido. No hay endpoint nuevo de perfil privado.
- No hay refresh tokens, cierre de sesión con revocación, recuperación, frontend ni activación de cuentas anteriores. Etapa 9.3 pendiente de revisión y solicitud.
- El error de credenciales es uniforme, pero no se igualan tiempos entre cuenta inexistente y verificación PBKDF2. No se añaden todavía limitación de intentos ni bloqueo: evaluar abuso y enumeración temporal antes de exponer el servicio.
- No registrar cuerpos, contraseñas, hashes ni cabeceras Authorization en aplicación, proxy o herramientas de observabilidad. El manejador global responde 500 genérico también en Development para evitar reflejar headers sensibles en la página de excepciones; conserva el diagnóstico interno estándar, sin habilitar logging sensible de EF.
