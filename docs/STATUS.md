# Estado

**Fase actual: 1 — Que sobreviva** (en marcha). Ver `PLAN.md`.

## Hecho

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

## Siguiente

- Paso 6: el juez de 24 h. Script en el PC que lee el registro extraído con `adb` y saca el hueco máximo entre
  líneas `capture`, reinicios, errores y la batería y temperatura mínima y máxima. La vigilancia que corre desde las
  05:42 UTC del 2026-10-23 con la versión y los ajustes finales de este paso es la que se juzga.
