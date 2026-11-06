# Preparación de producción — 10.7.4

No publica servicios ni ejecuta migraciones automáticamente. Una imagen contiene Angular
22 compilado y API .NET 10, bajo un único origen y puerto HTTP interno 8080.

## Construcción y reproducibilidad

Desde la raíz: `docker build -t helpdesk:local .`. Node/SDK solo existen en etapas de
construcción; la final contiene ASP.NET runtime y la publicación. `npm ci` usa el lockfile.
Para un release registrar commit, arquitectura, digest final y digests inmutables de
las tres imágenes base, pasados con `--build-arg NODE_IMAGE=...`, `SDK_IMAGE=...` y
`RUNTIME_IMAGE=...`. Los tags por defecto reciben actualizaciones y no garantizan
reconstrucción idéntica. No pasar secretos como build args ni incluirlos en el cliente.

## Configuración por entorno

Development conserva User Secrets y `ng serve` con proxy local. Testing usa únicamente
las fixtures y guardias de POSTGRESQL_TESTS.md. Production usa appsettings.Production.json,
rechaza AllowedHosts vacío/comodines y no carga User Secrets. Demo sigue desactivado;
activación y preparación separada exclusivamente según DEMO.md.

Montar un directorio privado de secretos en `/run/secrets:ro`, con archivos legibles por
UID 1654 (verificar UID en la imagen elegida), nombres `Jwt__SigningKeyBase64` y
`ConnectionStrings__DefaultConnection`. El proveedor KeyPerFile convierte `__` en `:`;
sus valores prevalecen sobre otras fuentes. No montar ahí configuración de desarrollo.
La conexión debe usar el rol limitado, TLS con `SSL Mode=VerifyFull` y CA confiable cuando
PostgreSQL no esté en una red local aislada. JWT: clave aleatoria externa de al menos
32 bytes Base64; conservar emisor/audiencia y arquitectura de autenticación existentes.
Variables no secretas en un archivo de operación externo:

```text
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=<nombre-dns-exacto>
Jwt__Issuer=<emisor>
Jwt__Audience=<audiencia>
ReverseProxy__KnownProxies__0=<ip-del-proxy-inmediato>
```

Ejecutar después de migrar y comprobar permisos:

```sh
docker run -d --name helpdesk --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --cap-drop ALL --security-opt no-new-privileges \
  --env-file /ruta/privada/produccion.env \
  --mount type=bind,src=/ruta/privada/secretos,dst=/run/secrets,readonly \
  -p 127.0.0.1:8080:8080 helpdesk:local
```

## Proxy, HTTPS y HTTP

El proveedor/proxy debe terminar TLS, redirigir HTTP público a HTTPS, validar Host,
reemplazar X-Forwarded-For y X-Forwarded-Proto con valores propios, y aislar el puerto
interno mediante red/firewall. Se admite un salto y una lista de IP exactas; sin lista
no se procesan forwarded headers. No usar ASPNETCORE_FORWARDEDHEADERS_ENABLED=true.
No se acepta X-Forwarded-Host. Reconfigurar IPs al cambiar proxy y probar antes de publicar.
El middleware se ejecuta antes de autenticación/cuotas; los cupos anónimos usan así la
IP validada. Cada instancia mantiene cuotas propias: varias requieren cuota en gateway.

HSTS solo para HTTPS reconocido; el proxy también debe emitirlo en sus errores. La API
no redirige HTTP interno para evitar bucles. Todas las respuestas de aplicación usan
no-store, incluidas privadas y errores. Desactivar caché de /api en CDN/proxy. Se añaden
nosniff, DENY, Referrer-Policy y Permissions-Policy. CSP limita marcos, objetos y base;
no restringe scripts/estilos con una política incompatible con Angular. Ampliarla exige
pruebas de navegador y nonces/hashes. Excepciones HTTP producen error genérico; logs
operativos son privados. No habilitar logging de cuerpos, tokens o SQL sensible.

## Migraciones como operación independiente

1. Crear una base exclusiva y roles separados mediante el administrador PostgreSQL.
   Propietario/migrador y runtime: LOGIN, NOSUPERUSER, NOCREATEDB, NOCREATEROLE,
   NOREPLICATION, NOBYPASSRLS; runtime no es miembro del propietario.
2. Revocar CONNECT/TEMP de PUBLIC en esa base y conceder solo CONNECT a ambos roles.
   Runtime no debe ser propietario de base, esquema, tablas ni secuencias.
3. Desde una estación/CI privada con SDK y dotnet-ef **10.0.12**, generar artefacto SQL:
   `dotnet ef migrations script --idempotent --project HelpDesk.Api -o /ruta/privada/migrations.sql`.
   Revisar el SQL y respaldar antes de aplicar. Configuración externa de diseño si se
   solicita; nunca usar credenciales habituales para preparar o validar artefactos.
4. Ejecutar SQL revisado con `psql -X -v ON_ERROR_STOP=1 -f ...` mediante conexión
   explícita a esa base, usando el migrador y un archivo pgpass privado (0600).
   No pasar contraseñas por argumentos. No ejecutar estos pasos en producción durante
   esta fase. No hay llamadas Migrate/EnsureCreated en la API.
5. Aplicar `deploy/runtime-grants.sql` como propietario; conceder CONNECT previamente.
   No otorgar privilegios por defecto sobre tablas futuras sin revisión. Rotar/retirar
   acceso del migrador tras la operación. Bootstrap y demo siguen como operaciones
   privadas independientes; sus secretos nunca entran en la imagen final.
6. Arrancar API con runtime, verificar readiness, login y permisos antes de habilitar
   tráfico. Una migración fallida bloquea la activación; no intentar reparar desde API.

## Sondas y resolución de errores

- `/api/health` conserva el contrato histórico de vida.
- `/api/health/live`: 200 indica proceso HTTP funcionando, sin consultar base.
- `/api/health/ready`: 200 si puede consultar las cuatro tablas necesarias; 503 genérico
  ante fallo, con cancelación de tres segundos. No muestra host, credenciales o SQL.

Configurar el orquestador para enviar tráfico solo tras readiness 200 y reiniciar solo
por liveness fallida. No usar readiness como liveness durante una caída PostgreSQL.
503: revisar red, TLS, esquema y grants en canal privado. Arranque rechazado: revisar
hosts/JWT y permisos de secretos. 404 en assets: confirmar salida browser de Angular.
401/403: comprobar configuración JWT y cuenta/rol actuales, sin registrar tokens.
429: respetar Retry-After; verificar IP del proxy y no ampliar confianza globalmente.
Rutas SPA sin extensión admiten acceso directo; archivos ausentes y /api desconocidas
no deben devolver index.html. GET y HEAD del frontend son públicos; API mantiene roles.

## Validación aislada

Usar siempre una instancia PostgreSQL nueva, volumen y red exclusivos, puerto loopback,
identidad Docker/TCP verificada y bases helpdesk_test_db/helpdesk_demo_db con sus guardias.
Nunca reutilizar una base habitual. Ver POSTGRESQL_TESTS.md. Probar imagen como no root,
read-only, sin capabilities; rutas SPA, assets, login, 401/403, demo desactivada y cuotas.
Parar PostgreSQL debe cambiar readiness a 503 manteniendo liveness 200. Inspeccionar
configuración/historial/filesystem de imagen para ausencia de secretos, SDK y Node.
La revisión visual en navegador, TLS y proxy reales requiere el entorno concreto y no
queda sustituida por comprobaciones HTTP locales. Resultados ejecutados en PROGRESS.md.

Referencias: [proxy ASP.NET Core 10](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
y [fallback Angular](https://angular.dev/tools/cli/deployment).
