# Historia

Las fases cerradas, tal como estaban en `STATUS.md` cuando se cerraron.

## Fase 1 — Que sobreviva (2026-10-22 a 2026-11-03)

### Paso 0 — El móvil y las reglas de Android (2026-10-22)

- Móvil leído por `adb`: OPPO A5 2020 (CPH1931), Android 10 (API 29), ColorOS 7.1, 3 GB de RAM, 4,8 GB libres.
  Mínimo soportado API 29; objetivo API 36 (DECISIONS 003).
- Verificado en la documentación oficial de Android: el tipo de servicio `camera` no existe en API 29 (llega en API 30),
  así que en este móvil el servicio se promociona sin tipo; la restricción "while-in-use" (Android 11), la prohibición de
  arrancar servicios desde segundo plano (Android 12) y la de arrancar servicios `camera` desde `BOOT_COMPLETED`
  (Android 15) no aplican en Android 10. `BOOT_COMPLETED` sigue siendo un receptor de manifiesto permitido.
  El riesgo real es ColorOS matando el proceso: ajustes del usuario (optimización de batería, inicio automático,
  ejecutar en segundo plano, candado en recientes), sin solución desde el código.
- Entorno: .NET 10.0.401 fijado con `global.json` (DECISIONS 004); los packs de Android 16 y las plantillas MAUI ya
  estaban instalados. `adb` está en `C:\Program Files (x86)\Android\android-sdk\platform-tools\`. El móvil solo aparece
  en `adb` con el USB en "Transferencia de archivos"; en modo MIDI es invisible.

### Paso 1 — Esqueleto (2026-10-22)

- Mismo esquema que CieloHud y VientoHud (DECISIONS 005): `VigiaHud.slnx`, `global.json`, `Directory.Build.props`
  (net10.0, nullable, avisos como errores), `.gitignore`, `.gitattributes`, `LICENSE` MIT, `THIRD-PARTY-NOTICES.md`,
  `licenses/` (OFL de Chakra Petch y Open Sans), `README.md` y CI de GitHub Actions (`Tests de Core`, solo Core y tests).
- `src/VigiaHud.Core` (vacío por ahora), `src/VigiaHud.App` (net10.0-android, mínimo API 29, `com.vigiahud.app`,
  versión 0.1.0 / 100) y `tests/VigiaHud.Core.Tests` (xUnit, un test humo que carga el ensamblado de Core).
- La app: tema oscuro de la familia (`Hud/HudTheme.cs`), fuentes Chakra Petch y Open Sans, icono y splash (un ojo en cian
  sobre `#0B1218`), y `WatchPage` con la cabecera, el estado de la vigilancia y el botón INICIAR (deshabilitado: se
  conecta al servicio en el paso 3). Solo Android: sin carpetas de iOS, Mac ni Windows.
- Validado: `dotnet build` de toda la solución (0 avisos, 0 errores, incluida la app Android) y `dotnet test` (1 test).
  `dotnet build src/VigiaHud.App -t:Install` instala en el OPPO (`minSdk=29 targetSdk=36 versionName=0.1.0` según
  `dumpsys package`); la app arranca sin errores en el registro (`logcat`): proceso vivo y ventana de `MainActivity`
  creada. El arranque en Debug tarda unos 15 s en este móvil. Captura con el móvil desbloqueado a mano (el bloqueo de
  ColorOS no se quita desde `adb`): `docs/verificacion/2026-10-22-fase1-paso1-esqueleto.png`.

### Paso 2 — Registro persistente (2026-10-22)

- `VigiaHud.Core.Logging` (DECISIONS 006): `LogKind` (`start`, `stop`, `error`, `capture`, `heartbeat`, `alert`,
  `info`), `LogEntry` (siempre UTC), `LogLine` (una línea por entrada: instante UTC con milisegundos, tipo y mensaje
  separados por tabulador; tabuladores y saltos de línea del mensaje escapados; `TryParse` para el juez),
  `IWatchLog`, `RotatingFileLog` (abre, escribe y hace `flush` a disco en cada línea; rota por tamaño, 1 MB y 5 ficheros
  por defecto; nunca lanza) y `LogReader` (lee los ficheros rotados en orden, salta líneas rotas, `ReadLast`).
- Paquete `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 solo en tests (`FakeTimeProvider`; MIT, de dotnet/extensions).
- La app registra `start` al abrirse (con la versión) y `error` en excepciones no controladas
  (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`). `WatchPage` muestra las últimas 8 líneas
  del registro en hora local (errores en rojo) con un chip ACTUALIZAR, para revisar el móvil sin ordenador.
- Dónde está en el móvil: `/data/user/0/com.vigiahud.app/files/logs/vigia.log`. Se lee con
  `adb shell run-as com.vigiahud.app cat files/logs/vigia.log` (compilación Debug).
- Validado: `dotnet build` (0 avisos) y `dotnet test` (26 tests: formato y parseo, ida y vuelta de mensajes con
  tabuladores y saltos, rotación con límite y ventana de ficheros, un solo fichero, escritura concurrente sin líneas
  partidas, fallo de disco sin excepción, lectura en orden saltando líneas rotas). En el OPPO, tras abrir la app dos
  veces, el fichero contiene dos líneas `start` con instante UTC correcto (hora local 17:03, UTC 15:03), y el panel
  REGISTRO las muestra en hora local: `docs/verificacion/2026-10-22-fase1-paso2-registro.png`.

### Paso 3 — Servicio en primer plano (2026-10-22)

- `Platforms/Android/Watch/WatchService` (DECISIONS 007): foreground service con notificación permanente
  ("VigíaHud · Vigilando.", canal "Vigilancia" de importancia baja, icono de ojo en la barra de estado),
  `PARTIAL_WAKE_LOCK` mientras corre, `START_STICKY`, nombre Java fijo. En Android 10 se promociona sin tipo;
  en 11+ con el tipo `camera`. Registra `start` ("service started" o "service restarted by Android"), `stop`
  ("service stop requested", "service destroyed"), `error` (fallo al arrancar) e `info` (app quitada de recientes).
- `IWatchController` en la app, con `AndroidWatchController` (intents con acción START/STOP). `WatchPage` conecta el
  botón INICIAR/PARAR, muestra "Vigilando." en verde o "Parada." y refresca el registro al cambiar el estado.
  En Android 13+ pide el permiso de notificaciones antes de arrancar; en el OPPO no hace falta.
- Manifiesto: `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CAMERA`, `WAKE_LOCK`, `CAMERA`, `POST_NOTIFICATIONS`
  y `uses-feature camera`.
- Validado en el OPPO con `adb`: tras pulsar INICIAR, `dumpsys activity services` muestra `WatchService` con
  `isForeground=true` y la notificación del canal `watch`; `dumpsys power` muestra `PARTIAL_WAKE_LOCK
  'VigiaHud::WatchService'`; el registro tiene `service started`. Tras PARAR desaparecen el servicio y el wake lock y
  el registro tiene `service stop requested` y `service destroyed`. `dotnet build` 0 avisos, `dotnet test` 26 tests.
  Captura: `docs/verificacion/2026-10-22-fase1-paso3-servicio.png`.
- Prueba corta de supervivencia: servicio arrancado a las 15:27:42 UTC, pantalla apagada (`mWakefulness=Asleep`)
  y comprobado a las 15:34:10 UTC: mismo proceso (pid 18347), `isForeground=true`, wake lock sostenido 6 min 28 s,
  sin reinicios en el registro. El juez de 24 h llega en el paso 6, con las capturas.

### Paso 4 — Captura periódica (2026-10-22)

- Core: `Watch/WatchTimings` (frame cada 2 s, línea `capture` cada minuto, `heartbeat` y foto de depuración cada
  10 min), `Watch/WatchReporter` (cuenta frames y fallos y decide cuándo toca cada cosa; la primera foto, al primer
  frame; si el móvil se retrasa, cada acción se dispara una vez y la siguiente se programa desde ahora),
  `Watch/WatchMessages` (`frames=30 failed=0 period=60s`; `uptime=1h02m battery=97% plugged=yes temp=33.5C`,
  cultura invariante), `Watch/SnapshotStore` (JPEG nombrados por instante UTC, máximo 200) y
  `Device/DeviceStatus` + `IDeviceStatusSource` (batería, cargador, temperatura).
- App: `CameraCapturer` (DECISIONS 008: Camera2, flujo YUV 320x240 al rango de fps más bajo, 15 fps en el OPPO;
  el servicio pide un frame cada 2 s y se convierte a JPEG; reapertura a los 30 s si Android quita la cámara),
  `AndroidDeviceStatusSource` (broadcast `ACTION_BATTERY_CHANGED`), y `WatchService` que crea el `WatchReporter`,
  pide frames cada 2 s y escribe `capture`, `heartbeat` y la foto (`files/snapshots/`). `WatchPage` pide el permiso de
  cámara antes de arrancar.
- Dos problemas resueltos en el OPPO: (1) `Image.Dispose()` no libera el hueco del `ImageReader` (excepción
  "maxImages (2) has already been acquired", que tiró la app; Android reinició el servicio solo, como se quería):
  hay que llamar a `Close()`; (2) las fotos sueltas (`StillCapture`) salían negro puro: cambiado a flujo continuo.
- Validado: `dotnet build` (0 avisos) y `dotnet test` (51 tests). En el OPPO, con la cámara apuntando a la
  habitación: `capture frames=30 failed=0 period=60s` cada minuto, foto real de 11 KB a 320x240 (con la orientación
  del sensor, sin girar; no se guarda en el repo porque sale el autor) y el registro en pantalla:
  `docs/verificacion/2026-10-22-fase1-paso4-captura.png`. La foto se lee con
  `adb shell run-as com.vigiahud.app cat files/snapshots/<nombre>.jpg`.
- Diez minutos con la pantalla apagada (`mWakefulness=Asleep`), del 16:03:25 al 16:13:58 UTC: una línea `capture`
  por minuto, todas `frames=30 failed=0`, `heartbeat uptime=10m battery=100% plugged=yes temp=30.0C` a los 10 min
  justos y segunda foto de depuración a los 10 min del primer frame. Sin errores ni reinicios.

### Primera muerte del servicio: ColorOS al desenchufar (2026-10-22, analizado el 2026-10-23)

- El servicio del paso 4 quedó vigilando el 2026-10-22 a las 16:03 UTC. El registro muestra 20 minutos perfectos
  (26 líneas `capture`, todas `frames=30 failed=0`, dos latidos) y se corta a las 16:23:29 UTC sin ninguna línea de
  parada. El latido de las 16:23:25 dice `plugged=no`: se había desenchufado el cable.
- Registro de eventos de Android (`adb logcat -b events`): `am_kill [... com.vigiahud.app, 1000, o-kill]` a las
  16:23:55 UTC (18:23:55 local), 26 s después del latido, seguido de `am_proc_died`. "o-kill" es el gestor de
  procesos de ColorOS. El móvil no se reinició (`/proc/uptime` 20 h). El servicio sticky no volvió: ColorOS mata y
  además impide el reinicio. La app no estaba excluida de la optimización de batería (`dumpsys deviceidle whitelist`
  vacío para ella) ni tenía los permisos de ColorOS de segundo plano.
- Conclusión: con la configuración de fábrica, ColorOS 7.1 mata la vigilancia al pasar el móvil a batería. El paso 5
  (exclusión de batería y ajustes de ColorOS) es obligatorio, y el juez tiene que incluir desenchufar y volver a
  enchufar el móvil.
### Paso 5 — Permisos y ajustes de ColorOS (2026-10-23)

- App: `Device/IPhoneSetup` con `AndroidPhoneSetup` (DECISIONS 009): comprueba `isIgnoringBatteryOptimizations`,
  pide la exclusión con el intent oficial (permiso `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` en el manifiesto) y abre
  la ficha de la app en Ajustes. Panel MÓVIL en `WatchPage`: estado en rojo o verde, chips EXCLUIR DE BATERÍA y
  AJUSTES DE LA APP; se refresca al volver de los diálogos del sistema (`Window.Resumed`). El servicio apunta al
  arrancar `info battery optimization ignored=yes|no`.
- `docs/MOVIL.md`: la guía con los nombres exactos de los menús del OPPO (ColorOS 7.1), encontrados a mano:
  la exclusión de batería desde la app (`verificacion/2026-10-23-fase1-paso5-dialogo-bateria.png`), "Uso de la
  batería > Ejecutar en segundo plano" en la ficha de la app (`...-coloros-segundo-plano.png`) y el inicio
  automático, que no está en Ajustes sino en la app Gestor del teléfono > Permisos de privacidad > Aplicaciones de
  inicio automático (`...-coloros-inicio-automatico.png`). El gestor de inicio no se puede abrir ni desde `adb` ni
  desde la app (`oppo.permission.OPPO_COMPONENT_SAFE`). ColorOS tampoco deja `pm grant` ni `settings put` desde
  `adb`.
- Validado en el OPPO (`verificacion/2026-10-23-fase1-paso5-movil-antes.png` y `...-despues.png`): tras aceptar el
  diálogo, `dumpsys deviceidle whitelist` incluye `com.vigiahud.app` y el panel pasa a verde. Con los tres ajustes
  (exclusión de batería, "Ejecutar en segundo plano", inicio automático) se repitió la prueba que ayer mató el
  servicio: cable desenchufado de las 05:57 a las 07:12 UTC (1 h 15 min, pantalla apagada). Registro continuo:
  una línea `capture` por minuto, todas `frames=30 failed=0` (una de 31 en 62 s), latidos cada 10 min con
  `plugged=no` y batería del 95 % al 86 % (unos 7 % por hora con la cámara en marcha), sin `error`, sin reinicio y
  sin `am_kill` en el registro de eventos. `dotnet build` 0 avisos, `dotnet test` 51 tests.
- Pendiente de probar: el candado en recientes y "limpiar todo" (el servicio no depende de la tarea, pero conviene
  comprobarlo en el juez de 24 h).

### Paso 6 — El juez de 24 h (en curso desde el 2026-10-23)

- `scripts/judge-log.ps1` (DECISIONS 010): extrae el registro del móvil y escribe el informe con el veredicto.
  Probado sobre la ventana parcial de hoy (05:42 a 07:17 UTC): 94 líneas `capture`, 2822 frames, 0 fallos, hueco
  máximo 1 min 01 s, 0 errores, 0 reinicios, 9 latidos (7 sin cargador), batería 85-96 %, temperatura 25,5-28,2 °C.
- La vigilancia que se juzga arrancó el 2026-10-23 a las 05:42:48 UTC con la versión y los ajustes del paso 5
  (exclusión de batería, "Ejecutar en segundo plano", inicio automático). Veredicto de 24 h: después de las 05:42
  UTC del 2026-10-24, con `powershell -File scripts\judge-log.ps1 -From 2026-10-23T05:42Z`. Si pasa, se deja hasta
  las 72 h (2026-10-26 05:42 UTC) antes de cerrar la fase. Mientras dure, no se reinstala la app ni se para el
  servicio; el móvil puede desenchufarse y volverse a enchufar.

### Paso 7 — Arranque tras reinicio y tras actualizar (2026-10-23)

- `BootReceiver` (DECISIONS 011): escucha `BOOT_COMPLETED` y `MY_PACKAGE_REPLACED` y arranca el servicio solo si
  la vigilancia estaba activa (`IWatchSettings.Enabled`, en `Preferences`; el controlador lo guarda cuando la petición
  de INICIAR sale bien y en PARAR). Deja `info` ("boot: service start requested") o `error` si el sistema no lo
  permite; el servicio comprueba el permiso de cámara al arrancar y se para con un `error` claro si falta.
  Requisito: móvil sin PIN ni patrón, porque `BOOT_COMPLETED` solo llega tras desbloquear (`docs/MOVIL.md`).
  Revisión de código del 2026-10-23 (7 hallazgos, todos en este paso): corregidos antes de instalar. Permiso
  `RECEIVE_BOOT_COMPLETED` en el manifiesto; el manifiesto generado lleva el receptor con los dos intents.
- Validado en el OPPO el 2026-10-23: reinstalación con el servicio en marcha (`adb install -r`): `update: service
  start requested` y `service started` 7 s después de la muerte del proceso; `adb reboot` a las 10:13 UTC:
  `boot: service start requested` y `service started` a las 10:15:36 UTC, con la exclusión de batería intacta y la
  alarma del vigilante armada. `dotnet build` 0 avisos, `dotnet test` 52 tests.

### Segunda muerte del servicio: la copia de seguridad de Android (2026-10-23)

- El juez intermedio de las 09:53 UTC dio FALLA: hueco de 15 min 44 s desde las 07:28 UTC y un segundo arranque
  manual. Eventos de Android: `full_backup_package com.vigiahud.app` a las 07:28:43 UTC y `am_proc_died` un segundo
  después: la copia de seguridad automática de Android (`allowBackup="true"`, el valor de la plantilla) mata el
  proceso para copiarlo, y de paso sube registro y fotos a la cuenta de Google. Android programó el reinicio sticky,
  pero aplicó `am_stop_idle_service` antes de que el proceso nuevo ejecutara el servicio y lo mató como vacío a los
  23 s. Causa de fondo del segundo fallo: la **exclusión de la optimización de batería había desaparecido sola**
  (`ignored=yes` a las 05:42 y 07:42 UTC, `no` a las 09:57; `dumpsys deviceidle whitelist` vacío); sin ella la app
  deja el cubo "exento" y Android para los servicios de una app inactiva al reiniciarlos. Reproducido: con la
  exclusión puesta, `am crash` con la app forzada a inactiva (`am make-uid-idle`) reinicia bien en 5 s
  ("service restarted by Android"); un `am crash` no quita la exclusión. Qué la quita sigue sin saberse: queda un
  vigilante observando `dumpsys deviceidle whitelist` y, desde esta versión, cada latido lleva `doze_exempt=yes|no`.
- Arreglos (DECISIONS 012): `allowBackup="false"`; `WatchdogReceiver` por alarma (`setAndAllowWhileIdle`, cada
  10 min) que arranca el servicio si no corre y la vigilancia estaba activa, y que se reprograma a sí mismo;
  cancelado en PARAR. `DeviceStatus.IsBatteryOptimizationIgnored` en Core (52 tests) y en el latido.
- Validado en el OPPO: tras instalar, `dumpsys package` ya no muestra `ALLOW_BACKUP`; la alarma `WATCHDOG` aparece en
  `dumpsys alarm`; una reinstalación con el servicio en marcha produce `update: service start requested` y
  `service started` en 7 s (receptor de `MY_PACKAGE_REPLACED`).

### Juez de 24 h: PASA (2026-10-25)

- Registro completo extraído: `verificacion/2026-10-25-0726-vigia.log` (3374 líneas). Informes del juez:
  `2026-10-25-fase1-juez-completo.md`, `-hasta-bateria.md` y `-24h.md`.
- **Hasta la batería** (2026-10-23 10:15 a 2026-10-24 03:11 UTC, 16 h 55 min): PASA. 1013 líneas `capture`, 30 394
  frames, 0 fallos, hueco máximo 1 min 02 s, 0 errores, 0 reinicios. 84 de los 101 latidos sin cargador: el móvil
  estuvo unas 14 h a batería, bajó al 1 % a la 01:47 UTC, aguantó así 1 h 24 min más y se apagó a las 03:11 UTC.
  Temperatura de 19,8 °C (batería agotada) a 36,0 °C (cargando con la cámara en marcha). Consumo a batería: unos
  5-6 % por hora con pantalla apagada y cámara a 15 fps.
- **Tras volver a encender** (2026-10-24 07:22 UTC en adelante): el receptor de arranque pidió el servicio
  (`boot: service start requested`, `service started` a las 07:22:48 UTC) sin tocar la app. A las 07:25 UTC del
  2026-10-25: **1 día 0 h 02 min** sin interrupción, 1439 líneas `capture`, 43 177 frames, 0 fallos, hueco máximo
  1 min 01 s, 0 errores, 0 reinicios, 143 latidos (37 sin cargador), batería 50-100 %, temperatura 23,8-31,0 °C.
  Veredicto del juez: PASA.
- Hallazgo: **la exclusión de la optimización de batería se pierde en el reinicio.** `doze_exempt=yes` desde el
  arranque del 23 hasta el apagado; `ignored=no` en el arranque del 24 y en todos los latidos posteriores;
  `dumpsys deviceidle whitelist` vacío. Es la causa más probable de las dos pérdidas del día 23 (la primera coincide
  con una copia de seguridad; queda la duda). El servicio sobrevivió las 24 h sin ella, con "Ejecutar en segundo
  plano" e inicio automático de ColorOS; pero sin la exclusión un reinicio sticky puede fallar (ver "Segunda muerte"),
  por lo que el vigilante por alarma sigue siendo necesario. Apuntado en `docs/MOVIL.md`: tras un reinicio, abrir la
  app y, si el panel MÓVIL está en rojo, volver a pulsar EXCLUIR DE BATERÍA. En la fase 2 el latido de Telegram
  debería avisar de `doze_exempt=no`.
- No se ha visto actuar al vigilante por alarma ni ha hecho falta: no hubo ninguna muerte del proceso.

### Juez de 72 h: FALLA por la cámara (2026-10-27)

- Registro: `verificacion/2026-10-27-1422-vigia.log` (7313 líneas). Informe: `2026-10-27-fase1-juez-72h.md`
  (2026-10-24 07:22 a 2026-10-27 07:22 UTC, 3 días exactos). 4299 líneas `capture`, 128 831 frames, 1 arranque,
  0 reinicios, 0 paradas; 148 de 430 latidos sin cargador (otra descarga a batería hasta el 9 %).
- Todo perfecto salvo **un incidente**: el 2026-10-26 a las 21:02 UTC la app de cámara de OPPO echó a VigíaHud de la
  cámara (`dumpsys media.camera`: `EVICT ... com.vigiahud.app`, la cámara de OPPO conectada de 21:02:13 a 21:02:16
  UTC). Coincide con desenchufar el móvil: el acceso rápido a la cámara se abrió al cogerlo. La vigilancia no
  recuperó la cámara hasta las 21:17:25 UTC: 3 huecos de 3 a 5 min entre líneas `capture` y 6 líneas con 0 frames.
  Veredicto: FALLA. Causa y arreglo en DECISIONS 013: sin cámara la CPU se durmió porque el wake lock del servicio ya
  no estaba en el sistema, y la reapertura dependía de un temporizador que no avanza con la CPU dormida.

### Arreglo: recuperar la cámara (2026-10-27)

- `CameraCapturer` reabre en cuanto la cámara queda libre (`AvailabilityCallback`), con un wake lock temporal
  mientras tanto; el wake lock principal se renueva cada minuto; el vigilante por alarma lo renueva y reabre la
  cámara si sigue perdida. Ocupada por otra app es `info`, no `error`.
- Validado por `adb`: el wake lock vuelve a aparecer en `dumpsys power`; con la app de cámara de OPPO abierta 4 s, la
  cámara vuelve a VigíaHud 0,2 s después de que la suelte; una sola línea `error` ("camera disconnected") por
  expulsión. `dotnet build` 0 avisos, `dotnet test` 52 tests.

- Prueba real desenchufado (2026-10-27, `verificacion/2026-10-27-fase1-prueba-desenchufado.md`): cable fuera de las
  15:03:33 a las 15:11:09 UTC, la app de cámara de OPPO abierta dos veces y la galería, pantalla apagada desde las
  15:04:55, reposo profundo (`device_idle=full`) desde las 15:05:56. La historia de batería de Android muestra el
  fallo de fondo en directo: a las 15:09:55 el sistema quita el wake lock `VigiaHud::WatchService`, la renovación de
  las 15:10:27 lo recupera y a los 2 s se lo vuelven a quitar (app sin la exclusión de batería). Aun así, **ningún
  hueco**: una línea `capture` por minuto con 30 frames durante todo el corte, porque las cuatro expulsiones de la
  cámara se recuperaron en 1-2 s y con el flujo en marcha el móvil no se duerme. El wake lock sigue registrado a los
  24 min del arranque: la renovación cada minuto funciona (sin ella caducaba a los 15). Veredicto del juez: PASA con
  avisos (las 5 expulsiones provocadas en las pruebas).
- Riesgo que queda: perder la cámara con el móvil ya en reposo profundo (no por la app de cámara, que exige encender
  la pantalla, sino por un fallo del propio hardware). Ahí el wake lock temporal también se perdería y la
  recuperación dependería del vigilante por alarma (10 min, más lo que retrase Doze). Con la exclusión de batería
  puesta no pasaría; la fase 2 debe avisar por Telegram de `doze_exempt=no`.

### Segundo juez de 72 h: roto por la batería; autonomía medida (2026-10-29)

- La vigilancia del arreglo (desde el 2026-10-27 14:47 UTC) no ha podido completar las 72 h: el móvil se desenchufó
  cada tarde y se quedó sin batería dos noches (apagado de 2026-10-27 ~23:59 a 2026-10-28 06:28 UTC y de 2026-10-29
  ~00:41 a 08:12 UTC). Las dos veces volvió solo al encenderse (`boot: service start requested`). Cada corte es un
  hueco de 6-7 h, así que el juez fallará por fuerza: `PLAN.md` pide el juez **enchufado**.
- Autonomía a batería con la cámara siempre en marcha: **8-9 % por hora** (49 % a 5 % en 5 h, dos días seguidos),
  unas 11-12 h desde el 100 %. Android lo atribuye casi entero a la vigilancia: en las 8 h 32 min a batería,
  `dumpsys batterystats` estima VigíaHud en 5323 de 6057 mAh calculados (cámara 4739, CPU 582) más 483 del servicio
  de cámara del sistema; pantalla 3, Wi-Fi 5, red móvil 9. Capacidad declarada 5000 mAh y descarga real medida
  2650-2750 mAh en ese tiempo (unos 320 mA de media): la batería está bien; el gasto es tener la cámara encendida.
  En el ordenador por USB el móvil carga a solo +4-5 % por hora con la cámara en marcha.
- Tercera muerte por ColorOS: `am_kill ... o-kill` el 2026-10-29 a las 10:48:52 UTC, enchufado al ordenador, con
  batería al 15 %, 36,8 °C y sin la exclusión de batería (perdida en el reinicio). El reinicio sticky la recuperó
  sola (`service restarted by Android` a las 10:53:24): hueco de 6 min. Exclusión puesta otra vez a las 11:34 UTC.

### Juez de 72 h en cargador de pared: PASA (2026-11-03)

- Registro: `verificacion/2026-11-03-0709-vigia.log` (18 023 líneas, todo desde el 2026-10-22: el registro rotó una
  vez). Informe: `verificacion/2026-11-03-fase1-juez-72h.md`.
- Ventana del 2026-10-29 11:50 al 2026-11-01 11:50 UTC (2 d 23 h 58 min), con la misma vigilancia que arrancó tras
  el apagado por batería del 29 (sin reinstalar), en un cargador de pared y con la exclusión de batería puesta otra
  vez: 4308 líneas `capture`, 129 259 frames, 0 fallos, hueco máximo 1 min 02 s, 0 errores, 0 arranques, 0 reinicios,
  0 paradas, 431 latidos todos enchufados, batería del 18 % (al empezar a cargar) al 100 %, 21,8-35,4 °C. Veredicto:
  **PASA**, sin avisos.
- Hasta el 2026-11-03 07:08 UTC: 4 d 19 h sin interrupción, 6901 líneas `capture`, 207 053 frames, 0 fallos, 0 errores;
  `doze_exempt=yes` en los 690 latidos: en estos 5 días la exclusión de batería no se perdió (se pierde al reiniciar).
- Los registros extraídos del móvil no se subían al repo (`*.log` en `.gitignore`); desde el cierre hay una excepción
  para `docs/verificacion/*.log` y están los tres que se conservaban (2026-10-25, 2026-10-27 y 2026-11-03).
- Riesgos que quedan, vigilados desde la fase 2: la exclusión de batería se pierde en cada reinicio (aviso por
  Telegram, DECISIONS 019 y 020) y, si la cámara se perdiera con el móvil ya en reposo profundo sin la exclusión, la
  recuperación dependería del vigilante por alarma (hasta 10 min).

## Fase 2 — Telegram (2026-10-29 a 2026-11-04)

### Qué funciona

- `VigiaHud.Core.Bot`: `IncomingMessage` (mensaje recibido: update, chat, texto, instante UTC), `BotCommand`
  (`/foto`, `/estado`, `/ayuda` y `/start`, desconocido), `CommandParser` (lee el comando tal como lo mandan los
  clientes de Telegram: con `@NombreDelBot`, con palabras detrás, en mayúsculas), `BotBrain` (decide: solo el chat del
  dueño recibe respuesta; cualquier otro se ignora sin contestar y deja una línea de registro con el chat y la longitud
  del texto, nunca el texto), `BotDecision` (ignorar, responder texto, mandar foto, mandar estado) y `BotTexts` (ayuda,
  "no te entiendo", estado y pie de foto en español, con la hora en la zona local que se le pasa; el estado avisa si
  falta la exclusión de batería).
- `VigiaHud.Core.Telegram` (DECISIONS 016 y 017): `ITelegramClient` con `TelegramClient` sobre `HttpClient` y
  `JsonDocument` (sin reflexión, apto para la app recortada): `GetUpdatesAsync` (long polling solo de mensajes, con
  offset; devuelve `UpdateBatch` con los mensajes y el offset siguiente, que avanza también con lo que no es mensaje),
  `SendMessageAsync` (JSON) y `SendPhotoAsync` (multipart con la foto JPEG y el pie). Cada petición lleva su propio
  plazo: el long poll más 15 s, o 60 s para enviar. Dos errores distintos para el paso 3: `TelegramApiException`
  (Telegram contestó con un error: código, descripción y `retry_after` si lo hay) y `TelegramUnavailableException`
  (sin red, sin respuesta a tiempo o una respuesta que no es de la Bot API). El token nunca aparece en un mensaje de
  error. Una cancelación desde fuera sigue siendo cancelación.
- `TelegramPoller` (DECISIONS 018): el bucle que mantiene al bot escuchando y no se rinde nunca. Entrega los
  mensajes en orden y solo los da por recibidos cuando están contestados: si contestar falla por falta de red, el
  mensaje vuelve a llegar. Sin Telegram espera 2 s, 4, 8… hasta 5 min, o lo que pida Telegram (`retry_after`); un
  token rechazado o un error inesperado esperan 5 min y se apuntan como `error`. Cada corte deja dos líneas en el
  registro (`telegram: offline (motivo)` y `telegram: online again after N s (M failed polls)`) y se entrega como
  `Outage` (inicio, fin, intentos, motivo) para avisar al dueño; si el aviso no sale por falta de red, se repite tras
  el siguiente corte. `BotTexts.Reconnected`: "Vuelvo a tener conexión. Estuve 12 min sin poder hablar con Telegram,
  desde hoy a las 11:00."
- Latido diario y cola de salida (DECISIONS 019):
  - `DailyHeartbeatSchedule` decide cuándo toca el "sigo vivo" de las 09:00 en la zona del móvil, con el cambio de
    hora; si uno no pudo salir a su hora sale en cuanto se puede, marcado como tardío. Una instalación nueva no debe
    nada hasta las 09:00 siguientes.
  - `Outbox` guarda en orden lo que el bot dice por su cuenta hasta que sale; si sale más de 2 min tarde dice de
    cuándo es ("(Con retraso: es de hoy a las 09:00.)"); máximo 50 mensajes.
  - `BotRunner` es el bot entero para la app: el bucle de Telegram contesta los comandos directamente (`/foto`,
    `/estado`, `/ayuda`) y el reloj, que se despierta al menos cada minuto y mira la hora real, escribe el latido y
    vacía la cola. Avisa al volver solo de los cortes de 5 min o más; todos cuentan en el resumen del latido.
    `IBotEnvironment` (zona, tiempo vigilando, estado del móvil, foto) y `IBotMemory` (último latido, que debe
    sobrevivir a reinicios) los dará la app en el paso 5.
  - Textos nuevos en `BotTexts`: `DailyHeartbeat` ("Sigo vivo." o "Sigo vivo (con retraso: a las 09:00 no pude).",
    tiempo vigilando, batería, cortes de las últimas 24 h y el aviso de exclusión de batería si falta) y
    `CameraNotReady`.
- Avisos de corriente y batería (DECISIONS 019 y 020): `PowerWatch` recibe cada minuto, del reloj de `BotRunner`, una
  lectura del móvil y dice qué contar, una vez cada cosa: `PowerNotice.Lost` a los 10 min sin cargador ("Sin
  corriente desde hoy a las 03:12: se ha desenchufado el cargador o se ha ido la luz. Batería al 84 %, unas 9 h de
  margen."), `Back` al volver si se avisó del corte ("Vuelve la corriente. Estuvo 1 h 20 min sin cargador; batería al
  72 %."), `LowBattery` al 15 % sin cargador ("me apagaré en unas 2 h") y `DozeExemptionLost` en cuanto falta la
  exclusión de batería, otra vez solo si vuelve y se pierde. Guarda todos los cortes de corriente (`PowerCut`), cortos
  incluidos, para el resumen del latido: "Últimas 24 h: sin cortes de conexión; 1 corte de corriente, de 4 min." El
  margen es `PowerWatch.HoursLeft`: el porcentaje entre 9 % por hora, redondeado a horas. Cada aviso deja una línea
  `power:` en el registro.
- En el PC: el bot existe (creado por el autor con @BotFather, sin poder unirse a grupos); el token y el chat ID del
  dueño están en los secretos de usuario con id `vigiahud`, fuera del repo. `scripts/record-telegram-fixtures.cs`
  graba respuestas reales de la Bot API con los datos personales sustituidos.
- En el móvil (paso 5, DECISIONS 021): `scripts/push-telegram-secrets.ps1` copia el token y el chat ID de los secretos
  del PC a `files/telegram.json`, en el almacenamiento privado de la app (permisos 600), sin fichero temporal ni nada
  en pantalla. `WatchService` lee ese fichero al arrancar (`TelegramSecrets` en Core) y pone en marcha `BotRunner` en
  su propio hilo; sin el fichero, la vigilancia sigue como en la fase 1 y lo apunta. `AndroidBotEnvironment` da la
  zona del móvil, la batería y el tiempo vigilando; `PreferencesBotMemory` guarda la hora del último latido.
  `/foto` toma una foto aparte de 1280×960 en la sesión de cámara que ya está en marcha (exposición ya ajustada, sin
  flash, con la orientación escrita en el JPEG: 90° para el móvil de pie); si la cámara no la da en 5 s, envía el
  último fotograma de la vigilancia. Cada respuesta deja `telegram: answered /estado|/foto|/ayuda|other text` en el
  registro, nunca las palabras del dueño.
- Menos CPU en la vigilancia: la conversión de cada fotograma a JPEG copiaba los colores píxel a píxel a través del
  puente con Java (unas 38 000 llamadas cada 2 s); ahora copia cada plano una vez. Medido con `/proc` en el OPPO, en
  ventanas de 20 s: la app pasa del 51 % al 21-23 % de un núcleo (el hilo de cámara, del 41 % al 10,6 %); el servicio de
  cámara del sistema pasa del 94 % (una medida) al 101-106 %, quizá por tener preparada la salida de la foto grande.

### Qué falta

- [x] Paso 1 — Comandos y dueño (2026-10-29).
- [x] Paso 2 — Cliente de Telegram propio con `HttpClient`, probado con respuestas reales grabadas (2026-10-29).
- [x] Paso 3 — Reconexión con espera creciente si cae el wifi o Telegram no responde, registrando cada corte
  (2026-10-29).
- [x] Paso 4 — Mensajes por su cuenta (DECISIONS 019), en dos partes:
  - [x] 4.1 Latido diario a las 09:00 con el resumen, con retraso si hace falta, y cola de salida (2026-10-29).
  - [x] 4.2 Avisos de corriente perdida (a los 10 min, con el margen estimado) y recuperada, batería baja al 15 % y
    exclusión de batería perdida; los cortes de luz cortos, solo en el resumen del latido (2026-10-29).
- [ ] Paso 5 — En el móvil (DECISIONS 021):
  - [x] Token y chat ID en el móvil fuera de git, el bot conectado al servicio, `/foto` a 1280×960 (2026-11-03).
  - [x] Latido diario con retraso, en el móvil (2026-11-03).
  - [ ] El primer latido diario a las 09:00 en punto (2026-11-04).
  - [x] Juez de la fase 2: 10 min sin red (wifi del móvil apagado por `adb`, en lugar del router); vuelve solo y avisa
    del corte. PASA en el segundo intento, tras los arreglos de DECISIONS 023 (2026-11-03).
  - [x] Fotos derechas en cualquier posición del móvil, con el acelerómetro (DECISIONS 022, 2026-11-03).

### Cómo se validó

- Paso 1: 50 tests nuevos en `tests/VigiaHud.Core.Tests/Bot` (`CommandParserTests`, `BotBrainTests`,
  `BotTextsTests`): comandos con y sin nombre del bot, mayúsculas y texto detrás; todo lo de un extraño ignorado y
  registrado sin su texto; chat de grupo con ID negativo; textos exactos del estado con y sin datos, con aviso de
  exclusión de batería; duraciones; "hoy", "ayer" y fechas en hora local con una zona de prueba fija. `dotnet build`
  0 avisos (app incluida), `dotnet test` 102 tests.
- Paso 2: 24 tests nuevos en `tests/VigiaHud.Core.Tests/Telegram`. `TelegramClientTests` con un `HttpClient` falso
  (`Support/FakeHttpHandler`): la petición de `getUpdates` (dirección, offset, plazo, solo mensajes), el primer
  sondeo sin offset, el offset que avanza también con lo que no es mensaje, JSON de `sendMessage`, multipart de
  `sendPhoto`, error 429 con `retry_after` (forma de la documentación: no se puede provocar sin riesgo), 502 en HTML
  como no disponible, fallo de red sin el token en el mensaje, plazo agotado, cancelación desde fuera y tokens mal
  formados. `TelegramRecordedAnswersTests` con seis respuestas reales grabadas el 2026-10-29
  (`Fixtures/Telegram`): los cuatro mensajes del dueño (`/start`, `/foto`, un emoji, que llega como texto, y una
  pegatina, sin texto) con las decisiones de `BotBrain`, el sondeo vacío, el texto y la foto enviados, el 401 de un
  token falso y el 409 de dos sondeos a la vez. Los tests cazaron un fallo real: `new Uri(base, "bot123:ABC/...")`
  toma "bot123" por protocolo y descarta la dirección de Telegram; la dirección se monta ahora como texto.
  Prueba de extremo a extremo con el bot real (script desechable): el cliente de Core lee, envía el texto y la foto
  y recibe el 401 con un token falso. `dotnet build` 0 avisos (app incluida), `dotnet test` 126 tests.
- Paso 3: 14 tests nuevos en `TelegramPollerTests`, con el reloj simulado (`FakeTimeProvider`), un Telegram de guion
  (`Support/FakeTelegramClient`) y un registro en memoria (`Support/MemoryLog`): orden de los mensajes y offset;
  offset que avanza con lo que no es mensaje; esperas 2, 4, 8… 256, 300, 300 s comprobadas al milisegundo; las dos
  líneas de un corte y su `Outage`; vuelta a 2 s tras reconectar; `retry_after`; token rechazado como error con 5 min;
  error inesperado sin salir del bucle; mensaje que vuelve si contestar falla por la red y que se salta si falla por
  un fallo nuestro; aviso de corte repetido tras otro corte o descartado si el fallo no es de red; cancelación limpia;
  texto de reconexión. `dotnet build` 0 avisos (app incluida), `dotnet test` 140 tests (tres pasadas seguidas).
  Sin prueba de red real: la da el juez de la fase 2 en el móvil (paso 5).
- Paso 4.1: 29 tests nuevos. `DailyHeartbeatScheduleTests` con una zona genérica con horario de verano: antes y
  después de las 09:00, a su hora con medio minuto de margen, latido perdido hoy o ayer, instalación nueva, las 09:00
  locales a ambos lados del cambio de hora de octubre y una hora que no existe en marzo. `OutboxTests`: orden, sin
  red se queda para la próxima, un mensaje rechazado para siempre no atasca la cola, nota de retraso solo a partir de
  2 min, capacidad. `BotRunnerTests` con el bot entero y un Telegram de guion: `/estado`, `/foto` con y sin cámara,
  extraño ignorado, latido a las 09:00 en punto y recordado, latido perdido enviado al arrancar como tardío, latido
  sin red que sale a los 3 min con la nota de retraso, y el umbral de 5 min de los avisos de corte. `BotTextsTests`:
  los textos del latido. Un test del bucle de Telegram falló una vez de cada muchas: adelantaba el reloj simulado
  antes de que el bucle empezara a esperar. `Support/SteppingClock` avisa cuando alguien espera y los tests solo
  adelantan después; cinco pasadas seguidas sin fallos. `dotnet build` 0 avisos (app incluida), `dotnet test` 169
  tests.
- Paso 4.2: 25 tests nuevos. `PowerWatchTests`: siempre enchufado no dice nada; 10 min sin cargador avisan una vez con
  desde cuándo y la batería; un corte corto no avisa pero queda para el resumen; el final de un corte avisado dice su
  duración; batería baja una vez al 15 %, otra vez solo tras cargar y volver a perder la corriente, nada si está
  cargando; exclusión perdida una vez, otra solo tras recuperarla; los cortes viejos se olvidan; el margen a 9 % por
  hora. `BotTextsTests`: los cuatro textos nuevos y el resumen con cortes de conexión y de corriente.
  `BotRunnerTests`: el aviso de corriente sale a los 10 min simulados, ni uno antes, y el de exclusión al arrancar.
  `dotnet build` 0 avisos (app incluida), `dotnet test` 194 tests (cinco pasadas seguidas).
- Paso 5 (2026-11-03): 14 tests nuevos (`TelegramSecretsTests`: el fichero bueno, chat de grupo, diez formas de
  fichero malo, entre ellas un chat ID entre comillas que hacía saltar una excepción, y que imprimirlo no muestra el
  token; `BotRunnerTests`: cada respuesta deja su línea sin las palabras del dueño). En el OPPO: el script copió los
  secretos (78 bytes, `-rw-------`; la primera vez llevaban delante la marca BOM que añade Windows PowerShell, ya
  evitada); tras instalar, `telegram: bot started` sin errores; el autor mandó `/estado`, `/ayuda`, un texto y `/foto`
  desde su Telegram y recibió el estado, la lista, el "No te entiendo" y la foto; con la foto grande, `/foto` llegó a
  1280×960 y derecha con el móvil de pie (`telegram: answered /foto`, sin respaldo). La vigilancia siguió con
  `frames=30 failed=0`. `dotnet build` 0 avisos (app incluida), `dotnet test` 208 tests (tres pasadas seguidas).
- Latido con retraso en el móvil (2026-11-03): en las preferencias de la app se puso como último latido el
  2026-11-01 a las 09:00 locales y se relanzó el proceso (`am crash`; Android lo relanzó en 3 s con
  `service restarted by Android`). Al arrancar, el bot escribió `telegram: daily heartbeat written (late)` y el autor
  recibió "Sigo vivo (con retraso: a las 09:00 no pude).", el tiempo vigilando (0 min, desde el relanzamiento), la
  batería y "Últimas 24 h: sin cortes de conexión ni de corriente.". Ese envío cuenta como el latido del día: el
  siguiente toca el 2026-11-04 a las 09:00 en punto.
- Orientación automática (2026-11-03): `PhotoOrientation` en Core (8 + 4 + 5 casos de prueba: de pie, tumbado a cada
  lado, boca abajo, torcido, empate a 45°, plano y el giro del JPEG); `GravityReader` en la app toma una sola lectura
  del acelerómetro por foto. En el OPPO, con `/foto` de pie, tumbado con la parte de arriba a la derecha y a la
  izquierda: el registro detectó 0, 90 y 270 y escribió 90, 180 y 0 en el JPEG; el autor recibió las tres derechas.
  Tiempos con la foto grande: la cámara la da en 0,6-1,0 s (187-224 KB) y la subida a Telegram tarda 0,6-0,7 s; unos
  envíos anteriores tardaron 8-20 s en total y no se repitieron con un wifi de -53 dBm a 433 Mbps: el registro
  apunta ahora el tamaño y el tiempo de la cámara para saber qué parte es la lenta si vuelve a pasar. `dotnet test`
  225 tests.
- Juez de la fase 2 (2026-11-03, `verificacion/2026-11-03-fase2-juez-wifi.md`): el autor no podía apagar el router y
  se apagó el wifi del OPPO por `adb` 10 min (sin datos móviles, se queda sin internet igual). Primer corte: volvió
  solo (`online again after 811 s`), contestó el `/estado` mandado durante el corte y el autor recibió el aviso de
  reconexión; pero el corte llegó como `WebException`, que el cliente no reconocía (línea `error` y 5 min de espera de
  golpe), y tardó 3,5 min en notar la vuelta del wifi. Arreglos (DECISIONS 023): cualquier fallo al hablar con
  Telegram es "sin red", y el bot reintenta en cuanto Android avisa de que hay red (`TelegramPoller.RetryNow`,
  `NetworkWatcher`). Segundo corte, con los arreglos: `offline` sin línea `error`, `network back; retrying now` y
  `online again after 606 s` 7 s después de volver el wifi, y `/estado` contestado. La vigilancia siguió con
  `frames=30 failed=0` en los dos cortes. Veredicto: PASA. 6 tests nuevos (`WebException`, `SocketException` e
  `IOException` como "sin red"; `RetryNow` corta la espera, no hace nada mientras sondea y no reinicia la espera
  creciente); `dotnet test` 231 tests (cinco pasadas seguidas).

### Cierre (2026-11-04)

- Primer latido diario en punto: `telegram: daily heartbeat written` el 2026-11-04 a las 08:00:00 UTC (09:00 local);
  el autor recibió "Sigo vivo.". Con el juez de 10 min sin red (2026-11-03) ya pasado, la fase queda cerrada.
- La noche anterior lo probó casi todo sin pedirlo: el móvil estuvo desenchufado de 17:10 a 06:26 UTC (13 h 16 min,
  batería del 100 % al 26 %). Avisos en el registro: exclusión de batería perdida a las 17:11 (sin reinicio:
  se pierde también al desenchufar), corriente perdida a las 17:20 (a los 10 min, con el 98 %), corriente recuperada
  a las 06:26. Sin red de 23:40 a 04:54 UTC (5 h 15 min, `online again after 18885 s`), probablemente el ahorro
  nocturno de ColorOS a batería; el bot volvió solo con `network back; retrying now`. La vigilancia siguió sin fallos
  (una sola línea `capture` con 31 frames en 62 s).
- Gasto de batería medido esa noche con la red activa: 5,2-5,3 % por hora (93 % a 67 % en 5 h; 62 % a 46 % en 3 h),
  frente al 8-9 % de la fase 1: la conversión de fotogramas sin llamadas a Java por píxel (DECISIONS 021) ahorra
  alrededor de un 40 %. Unas 18 h de autonomía desde el 100 %.
