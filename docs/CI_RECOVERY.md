# CI y recuperación — fase 10.7.5

Esta fase prepara validaciones; no publica imágenes ni despliega servicios. La ejecución
remota de GitHub Actions solo se considera validada cuando exista un run real en GitHub.
Resultados locales e incidencias en PROGRESS.md.

## Workflow

`.github/workflows/ci.yml` se activa con PR hacia main, push a main y workflow_dispatch.
Un job Ubuntu 24.04 hospedado por GitHub ejecuta secuencialmente:

1. Checkout sin credenciales persistentes. .NET SDK 10.0.114 (también en global.json),
   Node 24.15.0 y npm 11.12.1. No se instalan herramientas globales de EF para migrar.
2. Diff y siete pruebas Python de guardias de recuperación, sin base.
3. Restore NuGet `--locked-mode`, build Release, tests `Category!=PostgreSQL`.
4. `python3 scripts/ci_postgres.py`: instancia PostgreSQL 16 nueva y todas las pruebas
   `Category=PostgreSQL`; después backup real y restauración en otra instancia nueva.
5. `npm ci`, tests Angular sin watch y build production.
6. Construcción del Dockerfile de producción, sin login a registro, push ni despliegue.

Requisitos locales: Linux, Docker disponible, Python 3.12+, cliente psql/pg_dump 16,
.NET y Node/npm en las versiones indicadas. Ubuntu 24.04 de GitHub incluye Docker,
Python y cliente PostgreSQL; si cambia el runner, comprobar esos prerrequisitos.
No ejecutar PRs en un runner propio con acceso a bases, secretos o redes privadas.

Comandos para reproducir desde la raíz:

```sh
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet restore HelpDesk.NET.slnx --locked-mode
dotnet build HelpDesk.NET.slnx -c Release --no-restore
dotnet test HelpDesk.NET.slnx -c Release --no-build --no-restore --filter 'Category!=PostgreSQL'
python3 scripts/ci_postgres.py
npm --prefix HelpDesk.Web ci --no-audit --no-fund
npm --prefix HelpDesk.Web test -- --watch=false
npm --prefix HelpDesk.Web run build -- --configuration production
docker build -t helpdesk:ci .
git diff --check
```

Las pruebas temporales de cuotas requieren capacidad de CPU: evitar builds simultáneos
que puedan hacer transcurrir su ventana de dos segundos antes de terminar las peticiones.
No aumentar límites ni reintentar fallos automáticamente para conseguir un job verde.

## Aislamiento PostgreSQL

El script no acepta host, puerto, cadena de conexión ni contenedor existente. Crea nombres
UUID, volumen y red bridge exclusivos, contraseña nueva en archivo 0600/directorio 0700,
y puerto dinámico **127.0.0.1**. El clúster no contiene datos habituales. Comprueba:

- ID exacto de contenedor, digest de imagen, etiquetas, montajes y miembros de red.
- Puerto publicado únicamente en loopback.
- Identidad system_identifier y hora de arranque iguales por TCP y dentro del contenedor.
- Lista cerrada de nombres de bases, roles no privilegiados y UUID de la base demo.

Las guardias siguen activas incluso con `python -O` (no usan assert).
Solo después crea helpdesk_test_db/helpdesk_demo_db y pasa conexiones nuevas a las fixtures,
que repiten sus guardias antes de aplicar las nueve migraciones existentes. Se eliminan
variables heredadas de conexión/demo/JWT/PG y se fuerza Testing; no se leen User Secrets.
Nunca hay fallback a DefaultConnection. Las pruebas siguen usando sus roles, políticas
y limpieza existentes. El lanzador no contiene ninguna migración nueva.

La red bridge es exclusiva pero permite salida; no es un aislamiento frente a código
hostil con acceso al daemon Docker. Los dumps deben ser confiables. No hay directorios
habituales montados: solo volumen nuevo y archivo de contraseña efímera de lectura.
Al salir (incluido fallo normal) el context manager elimina solo sus contenedores,
volúmenes y redes, sin `docker system prune`. Durante interrupción forzosa puede quedar
un recurso; en local comprobar el nombre UUID y etiqueta helpdesk.isolation antes de
retirarlo. GitHub descarta el runner hospedado al finalizar. No subir su volumen o /tmp.

## Seguridad y reproducibilidad

GITHUB_TOKEN solo tiene `contents: read`; el resto de permisos no se conceden. No se
solicita OIDC, escritura, packages ni acceso a entornos de despliegue. No hay secrets
referenciados, `pull_request_target`, checkout privilegiado ni expresiones de títulos,
nombres de rama u otros textos de PR interpoladas en comandos shell.

Las tres acciones son oficiales `actions/*`, fijadas por SHA completo y con versión
comentada. Los SHA se contrastaron con sus tags oficiales. Son dependencias ejecutables:
revisar release/código y actualizar SHA deliberadamente; fijar no sustituye auditoría.
Antes de habilitar CI revisar también PRs que cambien workflow, scripts o Dockerfile.
El código de PR y los scripts de npm/MSBuild ejecutan código arbitrario en el runner;
no usar secretos, credenciales persistentes ni runners con acceso privado. Mantener
la aprobación manual para contribuciones externas según la política de GitHub.

No se guardan/restauran cachés Actions, node_modules, bin/obj ni paquetes NuGet entre
runs. setup-node desactiva su caché automática. Evita contaminación de caché por PR;
aumenta tiempo/red. Tampoco se suben artifacts, dumps, logs completos ni imágenes.
Contraseñas y cadenas efímeras se enmascaran con add-mask antes de ejecutar tests en
GitHub; procesos de aprovisionamiento/backup no imprimen argumentos ni errores sensibles.
No activar shell tracing ni volcar entornos. El enmascarado no protege frente a código
malicioso que transforme un valor: precisamente por ello solo hay credenciales efímeras.

NuGet usa cuatro packages.lock.json con hashes transitivos, Directory.Build.props y
restore bloqueado. npm ci usa el package-lock con integridad. Cambios de dependencias:
actualizar intencionadamente lockfiles y revisar diff, probar y revisar vulnerabilidades;
no borrar lockfiles para ocultar un fallo. Los digests de Docker/PostgreSQL se fijan y
no se actualizan solos; requieren revisión periódica de parches. SDK/Node/npm del job
están fijados. Las imágenes base mantienen sus propias versiones de SDK/Node; sus
contenidos quedan fijados por digest. El runner ubuntu-24.04 y registros externos
siguen evolucionando: esto no garantiza un build idéntico byte a byte ni disponibilidad
sin red. CI no sustituye una auditoría periódica de dependencias.

## Respaldos privados de PostgreSQL

`pg_dump` es una copia lógica consistente de una base, no un backup del clúster ni PITR.
Contiene correos, hashes de contraseña y contenido privado aunque no incluya JWT.
No guardarlo en Git, Actions, imágenes ni almacenamiento público. Cifrar con herramientas
de la organización, limitar accesos y retención y custodiar claves por separado.

La herramienta `scripts/recovery.py backup` es de solo lectura. Requiere servicio libpq
explícito, confirmación del nombre exacto de base y salida nueva fuera del repositorio.
No admite contraseñas por argumentos ni usa variables PGHOST/PGDATABASE heredadas.
Usa servicio/pgpass privados (0600), cliente pg_dump de la misma versión mayor que el
servidor, y default_transaction_read_only. Probar únicamente sobre la base aislada en
esta fase; un respaldo real futuro requiere autorización operativa del origen.

Preparar `pg_service.conf` fuera del repositorio (marcadores, no valores sensibles):

```ini
[origen-autorizado]
host=<host-explícito>
port=<puerto>
dbname=<base-explícita>
user=<rol-backup-con-lectura>
sslmode=verify-full
connect_timeout=5
```

Configurar CA cuando corresponda. Archivo pgpass externo: una línea
`<host>:<puerto>:<base>:<rol>:<secreto-externo>`, con escapes libpq necesarios y permisos
0600. No copiar ejemplos con secretos a terminal compartido o logs. Rol backup: CONNECT,
USAGE del esquema y SELECT de tablas/secuencias necesarias; sin DDL ni escritura.

```sh
export PGSERVICEFILE=/ruta/privada/pg_service.conf
export PGPASSFILE=/ruta/privada/pgpass
python3 scripts/recovery.py backup --service origen-autorizado \
  --confirm-source '<base-explícita>' --output /ruta/privada/copia.dump
sha256sum /ruta/privada/copia.dump > /ruta/privada/copia.dump.sha256
```

Crear antes un directorio privado 0700. La herramienta crea el dump 0600, no sobrescribe
archivos y elimina salidas parciales si pg_dump falla. El checksum detecta corrupción,
no acredita origen: custodiar checksum y archivo por un canal confiable. Mantener fuera
del dump inventario de versión PostgreSQL, commit, migraciones, extensiones, configuración
y procedimiento para recrear roles/grants. No se respaldan roles/passwords de clúster,
secretos de aplicación, certificados ni archivos del proveedor con este comando.

## Ensayo de restauración

Solo aceptar dumps confiables: un archivo puede contener SQL ejecutable. No restaurar
un dump ajeno en una instancia con acceso a información real. La herramienta no acepta
un destino existente y no tiene ruta para reemplazar una base habitual.

```sh
sha256sum --check /ruta/privada/copia.dump.sha256
python3 scripts/recovery.py restore --backup /ruta/privada/copia.dump \
  --confirm RESTORE-ISOLATED
```

Confirmación obligatoria antes de iniciar la operación. Se crea un clúster nuevo con el
mismo aislamiento verificado, base helpdesk_restore_db y propietario no superusuario,
sin membresías o privilegios elevados. pg_restore usa --no-owner --no-acl,
--exit-on-error y --single-transaction, nunca --clean/--create. Un destino ya existente
falla. No se aplican migraciones después de restaurar. Se comprueba que existen las
cuatro tablas y el historial EF, se leen todos los datos/secuencias y se exige que las
constraints estén validadas. El clúster se elimina al terminar, también tras errores.
La herramienta es un **ensayo**, no deja una base recuperada para servir tráfico.

El texto de los CHECK no es una representación canónica: PostgreSQL puede reescribir
casts equivalentes de arrays al restaurar. Se comparan inventario/tipos/columnas y
estado de validación de constraints, definiciones PK/FK/unique e índices. No se declara
equivalencia formal de expresiones CHECK por comparación de texto.

El job CI además compara la huella de todas las filas (incluidos hashes de cuentas),
migraciones, metadatos de constraints y definiciones de índices y valores/estado de secuencias del origen demo con la copia restaurada en
un segundo clúster; no imprime esas filas ni huellas. La prueba cubre un backup de la
versión actual, no garantiza compatibilidad con versiones o extensiones futuras.
Para recuperación operativa futura: restaurar primero aislado, verificar datos y
permisos, ensayar API/login con secretos exclusivos, validar RPO/RTO y planificar un
cambio de tráfico manual autorizado. No promover automáticamente esta base de ensayo.

**Restaurar base no es restaurar dataset demo.** pg_restore recupera la instantánea
completa, incluidos hashes/IDs/historial de migraciones. `HelpDesk.Demo --restore` de
DEMO.md conserva cuentas y hashes y reemplaza solo contenido ficticio según sus guardias
y confirmación UUID. No sirve como backup/recuperación de datos reales ni aplica esquema.

## Interpretar fallos y verificar en GitHub

- Restore locked: revisar csproj/lockfile, SDK y acceso a NuGet. No actualizar hashes a ciegas.
- Docker/TCP/guardias: detenerse; revisar Docker, psql, puerto loopback y metadatos del
  recurso recién creado. Nunca sustituir por la conexión habitual para hacer pasar CI.
- Integración: revisar test/nombre y errores seguros; se destruye el entorno al salir.
- Backup/restore: revisar versión cliente/servidor, formato e integridad en canal privado;
  no imprimir pgpass, dump o errores que incluyan filas. No desactivar constraints.
- Angular: revisar npm ci/lockfile/Node, tests o presupuesto del build.
- Imagen: revisar contexto, digests, restore y rutas de salida Angular. No publicar
  una imagen solo porque se construye: aún requiere revisión operativa.

Cuando el usuario autorice commit/push, abrir PR hacia main y verificar el check
**Build, tests and isolated recovery**; conservar enlace al run y resumen de resultados.
Después de incorporar el workflow en la rama predeterminada, ejecutar workflow_dispatch
y confirmar también un push real a main. Revisar Settings → Actions: permisos de lectura,
aprobación de forks y ausencia de secretos/entornos innecesarios. Configurar ese check
como obligatorio en protección de main solo tras observar una ejecución real correcta.
Esas acciones remotas siguen siendo manuales; no se han realizado en esta fase.

Para publicar la demo faltan proveedor/HTTPS/proxy real, secretos exclusivos, base y
roles de operación separados, política de backups/retención y ensayo RPO/RTO, prueba
visual de Angular y revisión final de exposición. No hay despliegue ni reset programado.

Referencias: [seguridad GitHub Actions](https://docs.github.com/en/actions/reference/security/secure-use),
[pg_dump 16](https://www.postgresql.org/docs/16/app-pgdump.html),
[pg_restore 16](https://www.postgresql.org/docs/16/app-pgrestore.html).
