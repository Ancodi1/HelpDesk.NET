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
