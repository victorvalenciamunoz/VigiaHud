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

## Siguiente

- Paso 4: captura periódica con Camera2 a baja resolución cada 2 s dentro del servicio, con un planificador en Core;
  línea `capture` por minuto con el número de frames y `heartbeat` cada 10 min con batería y temperatura;
  una imagen guardada cada 10 min para depurar.
