# Pruebas de tickets con PostgreSQL aislado

## Estado

Infraestructura y tres pruebas HTTP preparadas, compiladas y pendientes de ejecución sobre PostgreSQL. No se ha configurado `HELPDESK_TEST_CONNECTION_STRING` en la sesión de implementación. No se ha comprobado que exista la base de pruebas ni se han aplicado sus migraciones.

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

Pruebas sin PostgreSQL (las 17 anteriores y ocho casos de configuración que no abren conexiones):

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

xUnit crea una instancia de `TicketEndpointsTests` por caso. Cada una genera un título con GUID. La limpieza asíncrona elimina solo las filas cuyo título coincide con el GUID de esa instancia y comprueba que ya no existen, incluso si falla una aserción. No se vacían tablas, no se reinician secuencias ni se eliminan tickets de otros casos. Las tres pruebas comprueban POST 201 y persistencia con un contexto independiente, GET 200 de una fila preparada directamente en la base, y GET 404 de un ID previamente comprobado como ausente.

Si se interrumpe el proceso o se pierde la conexión, la limpieza no puede garantizarse: revisar manualmente solo los tickets `integration-<GUID>` de la ejecución afectada. No borrar globalmente por prefijo ni vaciar la base. Evitar ejecuciones simultáneas de la preparación de esquema; el aislamiento por GUID protege las filas, pero no coordina migraciones entre procesos distintos.

Los mensajes propios de configuración no incluyen cadenas ni contraseñas; la fábrica no registra parámetros SQL y desactiva sus proveedores de logs. No usar modos de diagnóstico que vuelquen variables de entorno o configuración sensible.

## Diagnóstico sin revelar secretos

La validación distingue mensajes de sintaxis ADO.NET incorrecta, opción no permitida, valor de opción inválido y restricciones de seguridad incumplidas. No incluye las claves desconocidas, sus valores ni excepciones internas. Un punto y coma sin citar puede separar accidentalmente la contraseña en otra opción o romper la sintaxis; no se puede reconstruir automáticamente la contraseña pretendida a partir de una cadena mal formada. Las comillas del shell y las comillas de la cadena ADO.NET son niveles distintos.

Para componer cadenas programáticamente, asignar Host, Database, Username y Password como propiedades de NpgsqlConnectionStringBuilder y obtener ConnectionString. El constructor aplica las comillas necesarias para conservar punto y coma, comillas y signo igual dentro del valor; no concatenar contraseñas. No imprimir el resultado ni la contraseña. La carga por read indicada arriba conserva el texto recibido, pero ese texto debe tener sintaxis ADO.NET válida.

Depuración del 20 de octubre: una prueba reprodujo el rechazo de la salida de Validate al volver a pasarla a Validate. La fixture normalizaba la cadena y la fábrica la validaba de nuevo; las opciones de seguridad añadidas no figuraban en la lista permitida. Corregido aceptándolas solo con false, manteniendo ambos controles. Las pruebas con contraseñas ficticias especiales pasan. La variable real no estuvo disponible en el proceso del agente; no se pudo inspeccionar su contraseña ni ejecutar los tres tests PostgreSQL. Este diagnóstico no atribuye a la contraseña real una causa no comprobada.
