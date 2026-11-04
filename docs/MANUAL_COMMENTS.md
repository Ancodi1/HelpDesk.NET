# Validación manual Angular — fase 10.6.2.1

Actualización operativa: se conserva este entorno para validar también el historial.
Consultar [MANUAL_ACTIVITIES.md](MANUAL_ACTIVITIES.md) para las nueve migraciones,
los tickets 1166–1169 y el lanzador recuperado sin secretos persistidos.

Entorno preparado el 4 de noviembre de 2026. Reutiliza exclusivamente
`helpdesk-1061-pg-33937133`, volumen `helpdesk-1061-data-33937133`, red
`helpdesk-1061-net-33937133`, PostgreSQL en `127.0.0.1:15436`.
Base `helpdesk_test_db`, rol limitado `helpdesk_test_user`.
Identificador del clúster: `7704264910117584940`.

La preparación original confirmó ocho migraciones, incluida
`20261104110125_AddTicketComments`. La ampliación de historial añade la novena,
AddTicketActivities, solo aquí. No se conectó a la base habitual.
El contenedor se deja arrancado.

El lanzador actual se ha recuperado en `/tmp/helpdesk-manual.py` (0700).
Las nuevas credenciales PostgreSQL y la clave JWT se generan únicamente en
memoria; no se guardan en archivos. El archivo de montaje requerido en
`/tmp/helpdesk-1061-isolated/owner-password` está vacío. Las contraseñas de
aplicación se eligen con entrada oculta y solo se conservan sus hashes.
Los archivos de /tmp son temporales: si faltan, recuperar el entorno antes
de continuar; nunca sustituirlo por la conexión habitual.

## Datos preparados

| Cuenta | Rol | Contraseña |
| --- | --- | --- |
| admin@manual.example.test | Admin | Pendiente de establecer localmente |
| technician@manual.example.test | Technician | Pendiente de establecer localmente |
| user@manual.example.test | User | Pendiente de establecer localmente |

| ID | Ticket | Creador | Asignado | Estado |
| --- | --- | --- | --- | --- |
| 1160 | Conversacion paginada | User | Technician | Open |
| 1161 | Sin asignar | User | Nadie | Open |
| 1162 | Asignado a Admin | Admin | Admin | Open |
| 1163 | Cerrado con comentarios | User | Technician | Closed |
| 1164 | Borrado permitido | User | Technician | Open |
| 1165 | Creado por Technician | Technician | Nadie | Open |

1160 contiene 25 comentarios numerados; los demás empiezan sin comentarios.
1163 permite comprobar que estar cerrado no impide publicar.
Las cuentas se crearon sin credencial: aún no permiten login.

## Contraseñas y arranque

Desde una terminal con acceso a Docker, sin activar `set -x`, ejecutar:

```bash
python3 /tmp/helpdesk-manual.py passwords
```

El procedimiento verifica contenedor, volumen, red, puerto, identidad TCP,
rol y migración. Pide cada contraseña dos veces sin eco (15–128 unidades
UTF-16, sin caracteres de control). Guarda solamente el hash Identity V3
HMAC-SHA512 con sal aleatoria y 210.000 iteraciones en las tres filas de
prueba; el texto plano permanece solo en memoria. No pasar contraseñas por
argumentos, chat, historial, variables escritas a mano ni archivos. Puede
repetirse para cambiar las contraseñas de estas cuentas exclusivamente.

Backend, en una terminal:

```bash
cd /home/angel/Documents/GitHub/HelpDesk.NET
python3 /tmp/helpdesk-manual.py api
```

El lanzador exige las tres credenciales establecidas, verifica nuevamente
el destino y ejecuta el DLL Debug ya compilado mediante `dotnet`, desde
el directorio HelpDesk.Api.
Solo el proceso hijo recibe la conexión aislada, la clave JWT exclusiva y
`ASPNETCORE_ENVIRONMENT=ManualIsolated`, con URL `http://localhost:5085`.
No carga User Secrets de Development ni modifica appsettings, perfiles o
configuración persistente. No ejecutar `dotnet run` directamente para esta
validación: usar siempre el lanzador. No ejecutar suites automáticas sobre
este entorno mientras se conserven los datos manuales.

Angular, en otra terminal:

```bash
cd /home/angel/Documents/GitHub/HelpDesk.NET/HelpDesk.Web
npm start -- --host localhost --port 4200
```

Abrir `http://localhost:4200`; el proxy existente dirige `/api` a 5085.
Cerrar previamente cualquier otra API que ocupe 5085: no cambiar el proxy
para conectarlo a una API de destino desconocido. Usar ventanas privadas o
perfiles separados para cada rol; cerrar sesiones antiguas de la base habitual.
No exportar HAR ni compartir cabeceras Authorization, cuerpos de login o tokens.

## Comprobaciones en navegador

1. Iniciar sesión como User. Abrir 1160 y pulsar **Abrir comentarios**.
   En Red, antes de pulsar no debe haber GET de comentarios. Después debe
   llegar una página de 20, ID descendente, `hasMore=true` y cursor no nulo.
   **Cargar anteriores** añade los cinco restantes sin duplicados; petición
   con `beforeId` exclusivo, respuesta `hasMore=false`, cursor nulo y sin
   ofrecer otra carga. No confundir esta paginación con la de tickets.
2. Publicar texto con varias líneas y texto literal como `<b>prueba</b>`:
   debe aparecer escapado, con saltos de línea, autor y fecha. Recargar y
   confirmar persistencia. El envío usa POST 201, autor de la sesión y fecha
   UTC del servidor; Angular presenta fecha local. Rechazar vacío, espacios
   y más de 4000 unidades UTF-16. El servidor también rechaza NUL.
3. En 1163, publicar un comentario aunque esté cerrado. Después su borrado
   debe quedar protegido también. Comprobar formulario bloqueado durante
   envío y ausencia de envíos duplicados. Ante error conservar borrador y
   recargar antes de repetir; no asumir que cancelar revierte el POST.
4. Cambiar entre cuentas y comprobar esta matriz inicial:

   | Rol | Tickets visibles y comentables |
   | --- | --- |
   | Admin | Todos |
   | Technician | 1160, 1163, 1164 (asignados), 1165 (creador) |
   | User | 1160, 1161, 1163, 1164 (creador) |

   Probar URL directa a 1162 con User/Technician: 404, sin contenido filtrado.
   Reasignar 1160 a Admin desde la cuenta Admin y volver a cargarlo con
   Technician: pierde lectura/publicación (404); User mantiene acceso por
   creador. Restaurar la asignación con Admin si se desea repetir.
   Para comprobar GET/POST de comentarios invisibles puede usarse **Editar
   y reenviar** en Red si el navegador lo permite, cambiando solo el ID a
   1162; no copiar tokens al chat. Sin sesión, la API debe responder 401.
5. Como Admin, intentar borrar 1160: HTTP 409 con
   `code=ticket_has_comments`; el ticket y sus comentarios se conservan.
   Borrar 1164 **antes de añadirle comentarios**: 204 y posterior 404.
   Esta es una eliminación intencional solo del ticket aislado identificado.
   User/Technician no deben disponer de borrado; una petición DELETE directa
   debe responder 403. No es necesario ejecutar DELETE SQL ni eliminar
   comentarios para esta validación.

Al terminar, parar Angular y API con Ctrl+C. Para detener solo PostgreSQL de
pruebas conservando el volumen:

```bash
docker stop helpdesk-1061-pg-33937133
```

Para otra sesión: `docker start helpdesk-1061-pg-33937133`, esperar a que esté
listo y ejecutar `python3 /tmp/helpdesk-manual.py verify` antes del lanzador.
No ejecutar `prepare` ni `migrate` durante la API activa. El lanzador actual
no contiene el antiguo comando `seed`.

## Validación realizada al preparar

Identidad Docker y TCP, base/rol limitado, ocho migraciones y datos iniciales
verificados con consultas reales. Sintaxis del lanzador comprobada mediante
`py_compile`. No se cambiaron fuentes de aplicación ni arquitectura; no se
repitieron builds o suites ya superadas en la fase anterior. No se ejecutó
ningún test ni migración sobre ninguna base. El arranque HTTP, login con las
contraseñas elegidas y revisión visual quedan pendientes del procedimiento
interactivo anterior. Sin commit ni push.
