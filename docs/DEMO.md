# Demo de portfolio — fase 10.7.3

Implementada el 6 de noviembre de 2026; desactivada por defecto. Se ha validado solo
con PostgreSQL Docker aislado. No se ha publicado, configurado la base habitual ni
creado credenciales permanentes de producción. No hay migración nueva.

## Decisión y alternativas

Se elige un modo demo explícito sobre la aplicación existente y una consola pequeña
para inicializar/restaurar contenido. Usa el mismo EF Core, hashing, JWT y políticas
de rol; no añade tablas, columnas, paquetes, repositorios ni capas.

Una bandera en Users exigiría migración y mezclaría usuarios reales con demo; proteger
solo Angular permitiría eludirla por HTTP. Recrear toda la base o ejecutarlo al arrancar
introduciría riesgo de borrado y pérdida del Admin. Una base por visitante añadiría
aprovisionamiento y arquitectura fuera de esta fase. Las cuentas compartidas y una
base exclusiva permiten una solución pequeña con limitaciones explícitas.

## Cuentas y permisos

| Identidad estable en la base demo | Rol | Uso |
| --- | --- | --- |
| ID 1, user@demo.helpdesk.test | User | Público: crear tickets, ver los propios, comentar |
| ID 2, technician@demo.helpdesk.test | Technician | Público: crear/ver los propios o asignados, comentar y cambiar estados permitidos |
| ID 3, owner@demo.helpdesk.test | Admin | Privado: administración, clasificación y asignación existentes |
| ID 4, Equipo Aurora | User | Personaje ficticio, sin credencial inicial |
| ID 5, Soporte Atlas | Technician | Personaje ficticio, sin credencial inicial |

Los tres primeros IDs quedan protegidos en backend contra escrituras de identidad,
incluso desde el Admin privado: 409/code=demo_account_protected. No se pueden cambiar
nombre, correo o rol por PUT. No existen rutas de cambio de contraseña, eliminación,
activación o recuperación: conservan 404/405; no se han incorporado. El guardia también
rechaza mutaciones de rutas de usuario con el mismo id cuando existen. Cualquier
funcionalidad de cuentas futura deberá revisarse expresamente contra esta protección.
Las cuentas normales fuera del modo demo conservan su funcionamiento y autorización;
las identidades no protegidas mantienen el PUT administrativo existente.

En demo, registro público y POST administrativo de usuarios devuelven
403/code=demo_registration_disabled. La autorización se ejecuta antes del guardia,
por lo que User/Technician siguen recibiendo 403 en administración y visitantes sin
JWT reciben 401 en escrituras protegidas. No hay un acceso especial que emita tokens
por rol: las cuentas públicas usan el login y la comprobación de cuenta/rol existentes.

Las contraseñas se suministran fuera de Git, nunca aparecen en esta guía y deben ser
distintas entre las tres cuentas, con la política vigente de registro. La consola usa
PasswordHasher<User> V3/210.000 iteraciones. La API solo necesita las dos contraseñas
públicas para ofrecerlas al navegador; **no recibe la contraseña Admin**. El endpoint
anónimo GET /api/demo devuelve únicamente enabled y las cuentas User/Technician si
está activo, con Cache-Control: no-store. En modo normal solo devuelve enabled=false.
Las dos credenciales públicas deben considerarse públicamente conocidas y exclusivas
de esta demo, aunque no se versionen. Nunca reutilizarlas en ninguna otra cuenta.

## Separación e identidad PostgreSQL

Demo:Enabled=true selecciona **solo Demo:ConnectionString**, sin fallback a
ConnectionStrings:DefaultConnection, appsettings de desarrollo o configuración del
bootstrap. La conexión exige exactamente helpdesk_demo_db y helpdesk_demo_user,
contraseña explícita, un único host y opciones de una lista cerrada. Se fija search_path
public y se desactivan diagnóstico sensible, persistencia de contraseña y Enlist.

La API al arrancar y la consola antes de escribir verifican por conexión real:

- current_database/current_user exactos.
- Rol sin SUPERUSER, CREATEDB, CREATEROLE, REPLICATION, BYPASSRLS o membresías.
- Comentario de base exactamente `HelpDesk.NET demo <UUID_EXCLUSIVO>`.
- Esquema preparado con las migraciones existentes; nunca migran automáticamente.

Además, el arranque verifica los tres IDs, roles/correos y hashes, y comprueba que las
contraseñas públicas publicadas verifican los hashes almacenados. Falla si no coinciden.
Desactivar el modo no permite abrir la base/rol demo por DefaultConnection.

El UUID no es una credencial: identifica el destino y requiere que el operador lo
marque al aprovisionar. El comentario es metadato PostgreSQL, sin cambios al modelo EF.
No aplicar esa marca a una base existente de trabajo. Las guardias impiden confusiones
accidentales; no pretenden impedir que un administrador de PostgreSQL malicioso
renombre bases, copie marcas o eluda la aplicación mediante SQL.

Usar una instancia PostgreSQL exclusiva, rol y secretos separados, sin red hacia la
base habitual. No dar al rol demo acceso a otras bases. Usar también secretos JWT,
issuer y audience propios del entorno público, separados de los habituales; no se ha
cambiado el formato ni la validación JWT. El origen público debe servir API/Angular
sobre HTTPS. Las condiciones de proxy y varias instancias de [SECURITY.md](SECURITY.md)
siguen siendo obligatorias; esta fase no configura ni despliega infraestructura.

## Aprovisionamiento inicial (operación futura en un destino exclusivo)

1. Crear una instancia PostgreSQL vacía y exclusiva. Verificar servidor/puerto,
   contenedor o recurso, volumen, red y ausencia de la base habitual antes de DDL.
   Crear únicamente allí el rol helpdesk_demo_user sin privilegios elevados, establecer
   su contraseña mediante entrada oculta/gestor de secretos y crear helpdesk_demo_db.
2. Generar un UUID exclusivo y fijar su marca con el administrador de **esa instancia**:

   ```sql
   COMMENT ON DATABASE helpdesk_demo_db IS 'HelpDesk.NET demo <UUID_EXCLUSIVO>';
   REVOKE CONNECT ON DATABASE helpdesk_demo_db FROM PUBLIC;
   GRANT CONNECT ON DATABASE helpdesk_demo_db TO helpdesk_demo_user;
   ```

   Sustituir el marcador por el UUID elegido, sin secretos. Restringir CONNECT en el
   resto de bases de esta instancia nueva; no modificar permisos de la base habitual.
   No asignar membresías al rol demo.
3. Cargar HELPDESK_DEMO_CONNECTION_STRING y HELPDESK_DEMO_INSTANCE_ID desde un entorno
   privado. La conexión apunta solo al recurso del paso 1. No usar argumentos con
   contraseñas, archivos versionados, `set -x`, volcados de variables o capturas.
4. Ejecutar la comprobación de identidad antes de preparar el esquema:

   ```bash
   dotnet run --no-build --project HelpDesk.Demo -- --check
   ```

   Preparar **por separado** las nueve migraciones ya existentes en ese destino,
   siguiendo el control de identidad de POSTGRESQL_TESTS.md. Esta consola no hace
   database update ni usa la conexión habitual. Si el esquema necesita una migración
   nueva, detener y solicitar revisión/autorización; no modificar el modelo para seed.
5. Suministrar HELPDESK_DEMO_USER_PASSWORD, HELPDESK_DEMO_TECHNICIAN_PASSWORD y
   HELPDESK_DEMO_ADMIN_PASSWORD desde el gestor de secretos o entrada local oculta.
   La base debe estar vacía; la inicialización rechaza cualquier contenido previo:

   ```bash
   dotnet run --no-build --project HelpDesk.Demo -- --initialize --confirm "$HELPDESK_DEMO_INSTANCE_ID"
   ```

   Los correos/IDs son los de la tabla. El proceso no imprime contraseñas/hashes y no
   reclama ni promueve usuarios existentes. Retirar la variable de contraseña Admin
   del entorno de la API después de inicializar. No confundir este mecanismo con el
   bootstrap de la aplicación habitual.
6. Configurar en el proceso de la API: Demo__Enabled=true, Demo__ConnectionString,
   Demo__InstanceId, Demo__UserPassword y Demo__TechnicianPassword, con los valores
   externos anteriores; proporcionar la configuración JWT pública separada. No pasar
   HELPDESK_DEMO_ADMIN_PASSWORD al proceso público. Arrancar solo tras revisar el
   destino. No se ejecutó esta operación contra un entorno público en esta fase.

Compilar previamente con `dotnet build HelpDesk.NET.slnx`. La consola no lee User
Secrets, DefaultConnection ni archivos de configuración de la API: únicamente las
variables HELPDESK_DEMO_* indicadas. Los mensajes de error son locales y seguros.

## Datos ficticios

Cinco identidades de laboratorio con dominios .test, sin personas ni empresas reales.
Ocho tickets cubren los cuatro estados, las cinco categorías, las cuatro prioridades,
asignaciones al técnico público/Atlas y tickets sin asignar. Hay casos visibles para
las cuentas públicas y uno de Aurora/Atlas que ambas reciben como 404, para demostrar
la autorización por recurso. Catorce comentarios y veintisiete actividades contienen
instantáneas de nombres ficticios y cambios de estado, prioridad, categoría y asignación.

Los ejemplos incluyen impresora, VPN, exportación de inventario, acceso a un aula,
proyector, catálogo, copia del simulador y guía de periféricos. Las fechas base son UTC,
15 de enero de 2026. El historial se construye expresamente como parte del dataset
ficticio, con secuencias coherentes; no se presenta como auditoría de hechos reales ni
se ofrece una ruta HTTP para insertar eventos arbitrarios.

## Restauración reproducible

Conservar únicamente las variables privadas de conexión/UUID para esta operación.
Repetir --check y después:

```bash
dotnet run --no-build --project HelpDesk.Demo -- --restore --confirm "$HELPDESK_DEMO_INSTANCE_ID"
```

Requiere UUID confirmado, base/rol exclusivos y marca verificada. Una confirmación
incorrecta se rechaza antes de conectar; una marca o identidad incorrecta antes de
borrar. No hay --force, DROP DATABASE, TRUNCATE, cascadas ni SQL de usuario.

Con el candado de escritura demo adquirido, borra actividades → comentarios → tickets
y reconstruye los ejemplos en **una transacción**. Una cancelación/error revierte las
operaciones de esa transacción. No borra usuarios, cambia hashes/roles o reinicia las
secuencias de tickets/comentarios/actividades. La inicialización avanza únicamente la
secuencia Users al máximo de los IDs iniciales; la restauración no la modifica.

Así se conservan el Admin privado y los accesos compartidos; un JWT previo sigue
representando a la misma cuenta hasta su caducidad normal. Las URLs de tickets borrados
quedan en 404 y sus IDs no se reciclan. Después de restaurar: recargar los listados y
no reenviar borradores obsoletos. Si se pierde conexión en commit, verificar contenido
y login antes de repetir; la operación es repetible, pero la consola no puede resolver
la incertidumbre de un commit cuya respuesta no llegó.

Si alguien con privilegios SQL o el Admin modifica los correos/roles de los personajes
4/5, la restauración rechaza identidades inesperadas antes de borrar. Revisar esa
intervención en el entorno demo; no arreglarla conectando a la base habitual ni
recreando toda la base. La restauración preserva nombres/cuentas y reconstruye solo
contenido, con sus instantáneas actuales.

## Cuotas, crecimiento y disponibilidad

Se conservan las cuotas 10.7.2: login 10/minuto e IP, registro 5/10 minutos e IP,
tickets 10/minuto y comentarios 30/minuto por cuenta. Cada cuenta pública comparte su
presupuesto entre visitantes. Pueden aparecer 429; no hay reintento automático ni
privilegio para saltarse las cuotas usando los botones de demo.

Además, el backend demo impone máximos globales de **100 tickets, 500 comentarios y
1.000 actividades**. Registro y creación administrativa cerrados evitan crecimiento
de usuarios por HTTP. Un candado PostgreSQL transaccional coordina cada escritura de
contenido con la restauración, también entre instancias, antes de comprobar capacidad.
La conexión de coordinación es separada para conservar las transacciones existentes
de los endpoints. Implica una conexión adicional por escritura y serialización solo
en demo; dimensionar conexiones/gateway para el pequeño tráfico esperado.

Al alcanzar el cupo, la operación responde 409/code=demo_capacity_reached; las lecturas
y login siguen disponibles. El máximo de actividades bloquea PATCH incluso si repetiría
el mismo valor. Si no se obtiene el candado en cinco segundos se devuelve
409/code=demo_busy. Angular muestra mensajes locales de capacidad/espera. No se crea
un endpoint público de reset ni un programador automático. El operador puede ejecutar
la consola periódicamente desde un entorno privado, con ese destino fijado; no se ha
instalado cron, servicio ni tarea remota. Revisar cupos y restaurar antes de compartir
el portfolio. Una visita puede consumir la capacidad de escritura compartida hasta la
restauración: no se garantiza disponibilidad ilimitada contra abuso.

Los máximos limitan filas vivas, no bytes físicos, logs o copias: mantener autovacuum,
retención de logs, supervisión/cuota de disco y límites en el proxy. Las escrituras
rechazadas o reintentos pueden avanzar secuencias sin crear filas. SQL externo no
participa en la garantía del middleware. La marca no constituye un secreto y el rate
limiter local no sustituye protección distribuida. Ninguno de estos límites cambia el
modo normal.

## Experiencia y comprobación manual

Angular consulta GET /api/demo al iniciar y no guarda credenciales en localStorage,
URLs o archivos estáticos. El banner identifica demo/datos ficticios en todas las
rutas y advierte de cambios compartidos y restauraciones. Si falla la consulta, avisa
que no se pudo comprobar el entorno y pide no introducir datos personales. En login,
User/Technician rellenan el formulario; el visitante confirma Iniciar sesión. No hay
botón Admin ni credencial administrativa en la respuesta. Registro se sustituye por
el enlace a cuentas públicas. El backend sigue siendo la autoridad.

En un destino manual **nuevo, aislado y previamente inicializado**:

1. Abrir inicio/login y elegir ambos roles, sin introducir datos propios. Comprobar
   banner, alcance por rol y texto ficticio. Verificar que GET /api/demo nunca contiene
   Admin, cadena de conexión, hash o configuración JWT y tiene no-store.
2. Como User, crear un ejemplo y comentar un ticket visible; estado y administración
   deben seguir en 403. Como Technician, ver asignados/propios, comentar y ejecutar una
   transición permitida. Aurora/Atlas queda en 404 para ambas cuentas.
3. Con el Admin privado, intentar PUT de correo/nombre/rol de IDs 1/2/3: 409 y login
   posterior conservado. POST de registro/usuarios se rechaza en backend. Rutas de
   password/eliminación de usuario siguen sin existir. No publicar esa sesión ni sus
   credenciales en capturas, enlaces o ayudas públicas.
4. Crear/modificar contenido; ejecutar --restore con la conexión exclusiva. Confirmar
   5 usuarios, 8 tickets, 14 comentarios, 27 eventos y logins conservados. Repetir y
   comprobar IDs de tickets nuevos, URLs anteriores 404 y ausencia de contenido visitante.
5. En copias de configuración de pruebas, intentar --restore con base habitual o
   confirmación incorrecta: debe fallar sin borrado. No usar credenciales habituales
   para probar el rechazo. Comprobar capacidades y recuperación mediante las pruebas
   automatizadas; no inundar la demo pública para validar máximos.
6. Arrancar el modo normal con su entorno aislado de pruebas y verificar registro/PUT
   normales, sin banner ni credenciales demo. Revisar visualmente móvil/escritorio.

Pruebas y resultados en [PROGRESS.md](PROGRESS.md) y [POSTGRESQL_TESTS.md](POSTGRESQL_TESTS.md).
Bases de la implementación: [transacciones EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions),
[bloqueos advisory PostgreSQL](https://www.postgresql.org/docs/16/explicit-locking.html#ADVISORY-LOCKS)
y [COMMENT](https://www.postgresql.org/docs/16/sql-comment.html).
