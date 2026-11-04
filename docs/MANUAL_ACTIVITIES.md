# Validación manual autenticada del historial — fase 10.6.2.2

Preparación realizada el 4 de noviembre de 2026. Se reutiliza el PostgreSQL manual de comentarios, sin ejecutar suites automáticas ni modificar la base habitual.

| Identidad comprobada | Valor |
| --- | --- |
| Contenedor | helpdesk-1061-pg-33937133 |
| ID Docker | fe84fb5c897470312a58f814de14f33865d79e4ee32b2ed0ed6c0e7cbd6e12fd |
| Volumen | helpdesk-1061-data-33937133 |
| Red exclusiva | helpdesk-1061-net-33937133 |
| TCP publicado | 127.0.0.1:15436 |
| Clúster PostgreSQL | 7704264910117584940 |
| Base / rol de aplicación | helpdesk_test_db / helpdesk_test_user |

El contenedor se deja arrancado. No se arrancó el contenedor automático `helpdesk-activities-pg-20261104` (15437). Se comprobaron los montajes, red con único miembro, puerto loopback, identidad interna y TCP, bases existentes y rol limitado sin membresías. El clúster contiene únicamente postgres, template0, template1 y helpdesk_test_db. Las nueve migraciones están aplicadas, incluidas:

- `20261104110125_AddTicketComments`, que ya existía.
- `20261104211800_AddTicketActivities`, aplicada en esta preparación exclusivamente al entorno manual verificado. Es aditiva; no reconstruye historial anterior.

## Lanzador recuperado y credenciales

`/tmp/helpdesk-manual.py` había desaparecido. Se recuperó con los comandos `verify`, `migrate`, `prepare`, `passwords` y `api`, sin cambiar fuentes de aplicación. La ayuda de migración está en `/tmp/helpdesk-manual-tool/`, también fuera de Git. Se restableció el archivo vacío `/tmp/helpdesk-1061-isolated/owner-password` necesario para el montaje existente; no contiene una contraseña y el volumen ya está inicializado.

No se guardan nuevas contraseñas de conexión, claves JWT ni tokens. Después de comprobar el clúster interno, cada verificación renueva en memoria la credencial del propietario aislado para contrastar la identidad TCP. `api` genera en memoria una credencial nueva para el rol limitado y una clave JWT exclusiva, y las entrega solo al entorno de su proceso hijo. Por eso no deben lanzarse dos API simultáneas contra este entorno ni ejecutar `migrate`/`prepare` durante la sesión manual. `verify` no cambia la credencial del rol de la API.

Las cuentas existentes se conservaron; las tres siguen sin credencial. Hay que establecer sus contraseñas localmente una vez, sin enviarlas al chat:

| Cuenta | ID | Rol | Nombre mostrado |
| --- | --- | --- | --- |
| admin@manual.example.test | 899 | Admin | Manual Admin |
| technician@manual.example.test | 900 | Technician | Manual Technician |
| user@manual.example.test | 901 | User | Manual User |

```bash
python3 /tmp/helpdesk-manual.py verify
python3 /tmp/helpdesk-manual.py passwords
```

Ejecutar en una terminal interactiva con acceso Docker, sin `set -x`. `passwords` solicita cada contraseña dos veces sin eco y valida 15–128 unidades UTF-16, sin controles ni solo espacios. Solo persiste el hash Identity V3 (HMAC-SHA512, sal aleatoria, 210.000 iteraciones) en esas tres cuentas aisladas. Las contraseñas permanecen en memoria; no se pasan por argumentos ni se escriben en archivos. Puede repetirse para esas cuentas si se olvidan. La compatibilidad del formato se comprueba con PasswordHasher de .NET, sin credenciales reales.

Los archivos de /tmp son temporales. Si faltan, detenerse y recuperar el entorno; no sustituir el lanzador por un arranque habitual o una conexión de desarrollo.

## Tickets preparados

Los tickets anteriores 1160–1165 y sus 25 comentarios de 1160 se conservaron. Se añadieron estos cuatro:

| ID | Uso | Creador / responsable inicial | Estado / prioridad / categoría | Actividades iniciales |
| --- | --- | --- | --- | --- |
| 1166 | Cambios y actualización tras guardar | Manual User / Manual Technician | Open / Medium / Other | 0 |
| 1167 | Paginación del historial | Manual User / Manual Technician | Open / High / Other | 25 |
| 1168 | Historial vacío y ausencia de retroactividad | Manual User / Manual Technician | Open / Medium / Other | 0 |
| 1169 | Ticket visible únicamente para Admin | Manual Admin / Manual Admin | Open / Medium / Other | 0 |

Las 25 actividades de 1167 son cambios de prioridad alternados High/Low mediante PATCH reales autenticados como Manual Admin, con el usuario de base limitado. No se insertaron actividades por SQL ni se reconstruyeron cambios anteriores. Para prepararlas se usó una API temporal y un JWT de prueba únicamente en memoria; ambos dejaron de existir al terminar. Se verificó por HTTP que la primera página contiene 20 filas y la siguiente cinco, sin más cursor. No es necesario ejecutar `prepare` de nuevo.

## Arranque exacto

Backend, en una terminal:

```bash
cd /home/angel/Documents/GitHub/HelpDesk.NET
python3 /tmp/helpdesk-manual.py api
```

Exige ambas migraciones y las tres cuentas con credencial. Si 5085 está ocupado, se detiene: cerrar primero la API anterior desde su propia terminal. Ejecuta el DLL Debug ya compilado con `dotnet`, en el directorio HelpDesk.Api, con `ASPNETCORE_ENVIRONMENT=ManualIsolated`, conexión limitada a 127.0.0.1:15436 y URL http://localhost:5085. No usa el perfil habitual ni User Secrets de Development. No ejecutar `dotnet run` directamente para esta prueba.

Angular, en otra terminal:

```bash
cd /home/angel/Documents/GitHub/HelpDesk.NET/HelpDesk.Web
npm start -- --host localhost --port 4200
```

Abrir http://localhost:4200/login. El proxy existente lleva /api a 5085. Usar ventanas privadas o perfiles independientes para cada rol y cerrar sesiones antiguas. Recargar Angular pierde la sesión, porque el JWT vive solo en memoria: volver a iniciar sesión y abrir el ticket. No exportar HAR ni copiar cuerpos de login, cabeceras Authorization o tokens.

## Comprobaciones desde Angular

1. **Vacío — 1168, User o Technician.** Abrir http://localhost:4200/tickets/1168 y pulsar **Abrir historial**. Debe mostrar «No hay actividad registrada todavía». No modificar este ticket si se quiere conservar la comprobación vacía. Los tickets anteriores 1160–1165 tampoco recibieron historial retroactivo.
2. **Autor, fecha y valores — 1166, Admin.** Abrir http://localhost:4200/tickets/1166 y dejar abierto el historial. Cambiar prioridad Media → Alta y categoría Otra → Red. Cada guardado correcto debe recargar el historial sin actualizar toda la página: Manual Admin, fecha/hora local y pares anterior → nuevo. Los comentarios deben seguir siendo independientes.
3. **Estado con técnico — 1166, Technician.** Antes de reasignarlo, entrar como technician@manual.example.test. Abrir historial y cambiar Abierto → En progreso → Resuelto; deben aparecer actividades de Manual Technician. Solo se permiten las transiciones del backend. User puede consultar el historial de su ticket, pero no gestionar esos campos.
4. **Responsable histórico — 1166, Admin.** Cambiar Manual Technician → Manual Admin → Sin asignar → Manual Technician. Comprobar los nombres anteriores/nuevos y «Sin asignar». Opcionalmente cambiar el nombre del técnico desde administración, volver al historial y comprobar que las entradas existentes conservan el nombre anterior; después restaurar el nombre. No usar el directorio para reinterpretar eventos pasados.
5. **Paginación — 1167, cualquiera de las tres cuentas.** Abrir http://localhost:4200/tickets/1167 y **Abrir historial**: 20 actividades con Manual Admin y prioridad alternada Alta/Baja, más recientes primero. **Cargar anteriores** debe añadir cinco (25 en total), sin duplicados, y desaparecer al terminar. Antes de esta comprobación no modificar 1167, para conservar el reparto exacto 20+5.
6. **Visibilidad — 1169.** Admin puede consultar http://localhost:4200/tickets/1169; User y Technician reciben el mismo mensaje de ticket inexistente/no disponible. Después de desasignar/reasignar 1166, un técnico que ya no sea responsable ni creador debe perder su visibilidad. Restaurar Manual Technician como responsable permite repetir sus comprobaciones.
7. **Respuestas antiguas y errores.** Con una carga pendiente (puede ralentizarse la red en DevTools), navegar entre 1166 y 1167: nunca deben mezclarse actividades. Detener solo la API con Ctrl+C y pulsar **Actualizar historial**: error recuperable sin borrar el detalle ya mostrado. Arrancar de nuevo con el mismo comando, cerrar/reabrir sesión porque la clave JWT temporal cambia y volver a cargar el historial.
8. **Repetición y presentación.** Guardar el mismo valor no debe generar otra actividad; si Angular impide enviar un formulario sin cambios, ese bloqueo es el resultado esperado en la interfaz. Comprobar escritorio/móvil y nombres con caracteres HTML como texto literal, sin marcado interpretado.

Se verificó el backend HTTP y sus datos durante la preparación. El login con contraseñas elegidas y la revisión visual autenticada de Angular quedan para esta sesión interactiva; no se declaran realizados.

## Parada y otra sesión

Detener API y Angular con Ctrl+C. Conservar el volumen y detener solo este PostgreSQL:

```bash
docker stop helpdesk-1061-pg-33937133
```

Para reanudar, si /tmp sigue disponible:

```bash
docker start helpdesk-1061-pg-33937133
python3 /tmp/helpdesk-manual.py verify
python3 /tmp/helpdesk-manual.py api
```

Esperar a que PostgreSQL esté listo antes de verify. No ejecutar suites automáticas contra esta base con datos manuales, ni volver a ejecutar `migrate`/`prepare` durante una API activa. No hubo cambios de arquitectura, fuentes de aplicación, datos habituales, commit ni push.
