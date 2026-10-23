# Decisiones

Formato: contexto → decisión → alternativas descartadas.

## 001 — Telegram como canal, sin backend
- Contexto: hacen falta avisos instantáneos con imágenes y comandos remotos, sin montar servidor.
- Decisión: Bot API de Telegram con long polling desde el propio móvil. Solo responde al chat ID autorizado.
- Alternativas: servidor propio con push (descartado: complejidad y mantenimiento);
  correo (descartado: lento y sin comandos).

## 002 — Análisis en el dispositivo
- Contexto: las imágenes son del interior de una vivienda.
- Decisión: toda la detección se hace en el móvil; solo salen las fotos que se envían al Telegram del usuario.
- Alternativas: servicios de visión en la nube (descartado por privacidad y dependencia de terceros).

## 003 — Dispositivo objetivo y versiones de Android soportadas
- Contexto: el móvil que se va a usar (leído por `adb` el 2026-10-22) es un **OPPO A5 2020 (CPH1931)**:
  Android 10 (API 29), ColorOS 7.1 (build CPH1931EX_11_C.96, parche de seguridad 2022-09-05),
  Snapdragon 665 (arm64-v8a), 3 GB de RAM, 47 GB de almacenamiento con 4,8 GB libres (90 % ocupado).
  El fabricante no ha publicado Android 11 para este modelo; no habrá más actualizaciones.
- Decisión: **mínimo Android 10 (API 29)**; objetivo el que trae .NET 10 por defecto (API 36, Android 16).
  Solo se valida en este móvil. El manifiesto se prepara también para las reglas de Android 11+, 14+ y 15+
  (tipo de servicio `camera`, permiso `FOREGROUND_SERVICE_CAMERA`) para que un cambio de móvil no rompa
  la app, pero esas versiones quedan como "no validadas".
- Consecuencias en Android 10 (comprobado en la documentación oficial):
  - El tipo de foreground service `camera` no existe en API 29 (se introdujo en API 30); en este móvil
    se promociona el servicio sin tipo. En 30+ se pasa el tipo `camera`.
  - No aplican la restricción "while-in-use" de Android 11, la prohibición de arrancar servicios desde
    segundo plano de Android 12 ni la de arrancar servicios `camera` desde `BOOT_COMPLETED` de Android 15.
    Es decir, en este móvil el servicio sí puede arrancar tras un reinicio y usar la cámara.
- Alternativas: mínimo API 21/24 (descartado: no hay otro móvil y añade código muerto);
  objetivo API 29 (descartado: no cambia nada en este móvil y .NET 10 avisa con objetivos antiguos).

## 004 — Entorno de compilación: .NET 10 fijado con global.json
- Contexto: en el PC el SDK por defecto es .NET 11 preview; también están 10.0.401 y los packs
  `Microsoft.Android.Ref.36` (API 36) para .NET 10.
- Decisión: `global.json` en la raíz fija el SDK 10.0.401 (rollForward a parches) para que `dotnet build`
  use siempre .NET 10 estable, como dice CLAUDE.md.
- Alternativas: usar .NET 11 preview (descartado: inestable y fuera del stack definido).

## 005 — Mismo esquema y estética que CieloHud y VientoHud
- Contexto: VigíaHud es el tercer "Hud" Android del autor. CieloHud y VientoHud (github.com/victorvalenciamunoz)
  ya tienen un esquema de repositorio y una estética probados.
- Decisión: copiar el esquema, no reinventarlo:
  - Raíz: `VigiaHud.slnx`, `global.json` (SDK 10.0.401, `rollForward: latestFeature`, sin previews),
    `Directory.Build.props` (net10.0, nullable, implicit usings, avisos como errores), `.gitignore` de `dotnet new`,
    `.gitattributes` (`* text=auto`), `LICENSE` MIT, `THIRD-PARTY-NOTICES.md`, `README.md` con la cabecera de la familia
    (icono, lema, badges), `licenses/` con las OFL de las fuentes, `scripts/publish-apk.ps1` (clave de firma en
    `%USERPROFILE%\.vigiahud\`, nunca en el repo).
  - Proyectos en `src/` y `tests/`: `VigiaHud.Core`, `VigiaHud.App` (net10.0-android, `SingleProject`,
    `MauiXamlInflator=SourceGen`, `EmbedAssembliesIntoApk` en Debug, `ApplicationVersion` = major*10000+minor*100+patch),
    `VigiaHud.Core.Tests` (xUnit 2.9, `coverlet.collector`, `Using Include="Xunit"`). `VigiaHud.Console` llega en la fase 3.
  - CI de GitHub Actions (`Tests de Core`): solo Core y sus tests en ubuntu, sin workload MAUI. La app se prueba a mano
    en el móvil. `main` protegida: cada paso entra por rama y pull request.
  - Estética: `Hud/HudTheme.cs` con la paleta por roles (fondo `#0B1218`, texto en cuatro niveles, `Good #7CFFB2`,
    `Guide #4DD2FF`, `Marker #FFC42E`, `Alert #FF5C5C`) publicada como `DynamicResource`; tema siempre oscuro;
    fuentes Chakra Petch (títulos, chips en mayúsculas con `CharacterSpacing`) y Open Sans (texto); icono y splash
    sobre `#0B1218` con el motivo en cian; botones "chip" con borde de 1 px y radio 6.
  - Código: carpeta por feature en Core con namespace igual a la carpeta; `record`/`record struct` inmutables;
    comentarios XML en inglés que explican el porqué; tests con nombres `Hace_tal_cosa` en snake case y `Theory` con
    `InlineData`; interfaces para todo lo de plataforma con implementaciones falsas en tests.
- Alternativas: plantilla MAUI por defecto (descartado: estética y estructura distintas a las de la familia).

## 006 — Registro propio en fichero, una línea por entrada, rotación por tamaño
- Contexto: el registro es el juez de la fase 1 (uptime medido) y tiene que sobrevivir a que Android mate el proceso
  sin avisar. Lo leerán un script del juez (huecos entre capturas) y la propia app (últimas líneas en pantalla).
- Decisión: `VigiaHud.Core.Logging` sin paquetes: `RotatingFileLog` escribe una línea por entrada
  (`instante UTC con milisegundos` TAB `tipo` TAB `mensaje`, tabuladores y saltos de línea escapados), abre el fichero
  en cada escritura y hace `flush` a disco (sin handle abierto que perder), rota por tamaño (1 MB) y guarda 5 ficheros
  (`vigia.log`, `vigia.1.log`…). Un fallo al escribir se traga: el registro nunca tumba la vigilancia.
  `LogReader` lee los ficheros rotados en orden y salta líneas rotas. Reloj inyectado (`TimeProvider`).
  Los tipos son fijos: `start`, `stop`, `error`, `capture`, `heartbeat`, `alert`, `info`.
- Alternativas: `Microsoft.Extensions.Logging` con un proveedor de fichero (Serilog, NLog…): un paquete más en un
  móvil viejo y un formato pensado para humanos, no para un script; `Console`/`Debug` (descartado: no persiste).

## 007 — El servicio de vigilancia: primer plano, wake lock parcial, sticky, sin tipo en Android 10
- Contexto: la vigilancia tiene que seguir viva con la pantalla apagada durante días en un OPPO con Android 10
  (DECISIONS 003). Verificado en la documentación oficial de Android el 2026-10-22.
- Decisión: `WatchService` (nombre Java fijo `com.vigiahud.app.WatchService`) es un foreground service con
  notificación permanente en un canal de importancia baja ("Vigilancia", icono de ojo), `PARTIAL_WAKE_LOCK` sin
  tiempo límite mientras corre (liberado en `OnDestroy`) y `START_STICKY` para que Android lo reinicie si lo mata;
  `stopWithTask` en falso para que deslizar la app en recientes no lo pare (queda en el registro como `info`).
  En el manifiesto va `foregroundServiceType="camera"` con los permisos `FOREGROUND_SERVICE`,
  `FOREGROUND_SERVICE_CAMERA`, `WAKE_LOCK`, `CAMERA` y `POST_NOTIFICATIONS`; en el código el tipo `camera` solo se
  pasa a `StartForeground` en Android 11+ (`OperatingSystem.IsAndroidVersionAtLeast(30)`), porque en API 29 no
  existe. Arranque y parada por intents con acción desde la página (`StartForegroundService`), siempre con la app
  visible, que es lo que exige Android 12+. Cada arranque, parada, reinicio por Android y fallo deja una línea en el
  registro (DECISIONS 006).
- Alternativas: `WorkManager`/`JobScheduler` (descartado: no garantizan continuidad ni acceso a la cámara);
  wake lock con tiempo límite renovado (descartado por ahora: un olvido lo apaga; se revisará si la batería o el
  juez lo piden); pantalla encendida en negro (plan B de `PLAN.md`, solo si el juez de 24 h falla).

## 008 — Captura con Camera2: flujo continuo a baja resolución y pocos fps, un frame cada 2 s
- Contexto: la fase 1 solo necesita demostrar que la cámara sigue entregando frames con la pantalla apagada durante
  días; el análisis llega en la fase 3. Móvil viejo y enchufado 24/7: cuanto menos trabaje la cámara, menos calor.
  Primer intento (2026-10-22): una foto JPEG suelta (`StillCapture`) cada 2 s desde una sesión sin flujo previo.
  Funcionaba (30 frames por minuto, cero fallos) pero todas las fotos salían negro puro: sin flujo la exposición no
  converge en este móvil.
- Decisión: `CameraCapturer` abre la cámara trasera con Camera2 en su propio `HandlerThread` y mantiene una
  petición repetitiva (`Preview`, sin flash, el rango de fps más bajo que ofrece la cámara) hacia un único
  `ImageReader` YUV_420_888 al tamaño más pequeño de al menos 320 px de ancho (320x240 en el OPPO). El servicio pide
  un frame cada `WatchTimings.CaptureInterval` (2 s): el siguiente frame del flujo se convierte a NV21 y a JPEG
  (calidad 70) y se entrega; el resto se descartan sin copiarlos. Si al pedir el siguiente aún no ha llegado el
  anterior, el flujo se ha parado: cuenta como fallo. Si Android quita la cámara (`onDisconnected`/`onError`), se
  cierra y se reintenta a los 30 s; cada fallo es una línea `error` y suma en `failed` de la siguiente línea
  `capture`. `WatchReporter` (Core) decide cuándo toca la línea `capture` (cada minuto: `frames=N failed=M
  period=60s`), el `heartbeat` (cada 10 min: `uptime battery plugged temp`) y la instantánea de depuración (la
  primera y después cada 10 min; `SnapshotStore` guarda como máximo 200 en `files/snapshots`, con la orientación del
  sensor, sin girar). Permiso de cámara pedido en la página antes de arrancar el servicio.
- Alternativas: fotos sueltas (descartado: negras, ver arriba); ráfaga por petición para que converja la exposición
  (descartado: más trabajo por foto que un flujo a pocos fps y no sirve para la fase 3); CameraX (descartado:
  paquete AndroidX más sin ventaja); API `Camera` antigua (obsoleta).

## 009 — La exclusión de la optimización de batería se pide desde la app; el resto de ColorOS, a mano con guía
- Contexto: con la pantalla apagada Android entra en reposo (Doze) e ignora los wake locks de las apps que no están
  en su lista de exentas; ColorOS añade sus propios interruptores (inicio automático, segundo plano, candado en
  recientes) sin API. Verificado en la documentación oficial de Android (`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`,
  `isIgnoringBatteryOptimizations`) el 2026-10-22.
- Decisión: la app declara `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` y, desde el panel MÓVIL, muestra si está excluida
  (`PowerManager.isIgnoringBatteryOptimizations`) y lanza el diálogo oficial para pedirlo; también abre la ficha de
  la app en Ajustes para los interruptores de ColorOS. El servicio apunta al arrancar `battery optimization
  ignored=yes|no` para que el juez sepa en qué condiciones corrió. La guía con los nombres de los menús del OPPO está
  en `docs/MOVIL.md`. La política de Google Play que restringe ese intent no aplica: la app no se distribuye por Play.
- Alternativas: solo abrir la pantalla general de optimización de batería (`ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS`,
  sin permiso) y que el usuario busque la app (descartado: más pasos, más fácil equivocarse); no pedir nada y confiar
  en el foreground service (descartado: el wake lock de un servicio en primer plano sobrevive a Doze en el código de
  Android, pero en ColorOS no hay garantía y el coste de pedirlo es un toque).

## 010 — El juez de la fase 1 es un script sobre el registro, con umbrales fijos
- Contexto: `PLAN.md` pide 24 h (y después 72 h) con la pantalla apagada y "sin huecos mayores de 1 minuto" en las
  capturas, medido sobre el registro. El registro ya escribe una línea `capture` por minuto con los frames de ese
  minuto, así que un hueco en los frames se ve como una línea con pocos frames o como un hueco entre líneas.
- Decisión: `scripts/judge-log.ps1` (PowerShell, sin proyecto nuevo: la consola de Core llega en la fase 3).
  Extrae el registro del móvil con `adb` (`run-as`, ficheros rotados en orden) o lee uno dado, filtra una ventana
  UTC y escribe un informe Markdown: ventana, líneas `capture` y frames totales, hueco máximo entre líneas
  `capture`, huecos mayores de 120 s (dos minutos perdidos; la cadencia nominal es 60 s y se han visto 62 s
  normales), líneas con menos de 25 frames o con fallos, arranques y reinicios por Android, paradas, errores, latidos
  (último uptime, cuántos sin cargador), batería y temperatura mínima y máxima. Veredicto: PASA si no hay huecos,
  reinicios, errores ni líneas flojas; PASA con avisos si solo hay errores o líneas flojas; FALLA si hay huecos o
  reinicios. El registro extraído y el informe se guardan en `docs/verificacion/` con la fecha.
- Alternativas: implementar el juez en Core con tests (descartado por ahora: necesitaría un host de consola que el
  plan reserva para la fase 3; el formato es trivial de parsear); juzgar a ojo (descartado: el juez son los números).

## 011 — Arranque tras reinicio y tras actualizar la app, solo si la vigilancia estaba activa
- Contexto: un corte de luz largo apaga el móvil cuando se agota la batería; al volver la luz arranca solo, pero la
  app no. Lo mismo al instalar una versión nueva: Android mata el proceso y el servicio no vuelve. Verificado en la
  documentación oficial (2026-10-22): `BOOT_COMPLETED` sigue siendo un receptor de manifiesto permitido; en Android
  10 un receptor puede arrancar un servicio en primer plano y usar la cámara; en Android 15+ (objetivo 35+) arrancar
  un servicio de tipo `camera` desde `BOOT_COMPLETED` lanza `ForegroundServiceStartNotAllowedException`. En ColorOS
  hace falta además el permiso de inicio automático (`docs/MOVIL.md`).
- Decisión: `BootReceiver` (nombre Java fijo, exportado como exige el sistema para estos broadcasts) escucha
  `BOOT_COMPLETED` y `MY_PACKAGE_REPLACED`. Solo arranca el servicio si la vigilancia estaba activa:
  `IWatchSettings.Enabled` (en `Preferences`, `watch_enabled`), que el controlador pone a verdadero cuando la
  petición de arranque de INICIAR sale bien y a falso en PARAR; un reinicio del servicio por Android no lo toca.
  Deja en el registro `info` ("boot: service start requested"; no `start`, que es la línea del propio servicio y la
  que cuenta el juez) o `error` si el sistema no lo permite; si no hay registro, lo dice en logcat. El servicio
  comprueba el permiso de cámara al arrancar por cualquier vía y, si falta, lo apunta y se para en vez de reintentar
  la cámara cada 30 s sin decir por qué.
  Requisito del móvil: **sin PIN ni patrón**. Android solo entrega `BOOT_COMPLETED` cuando el usuario ha
  desbloqueado (almacenamiento cifrado con credenciales), así que con bloqueo seguro el arranque desatendido no
  ocurre. Soportar `LOCKED_BOOT_COMPLETED` exigiría mover registro y preferencias a almacenamiento protegido por
  dispositivo; se descarta en esta fase y se documenta en `docs/MOVIL.md`. En este
  móvil (Android 10) debe funcionar; en Android 15+ quedará apuntado el error y el usuario tendrá que abrir la app.
- Alternativas: arrancar siempre al reiniciar (descartado: si el dueño paró la vigilancia a propósito, debe seguir
  parada); `WorkManager` para diferir el arranque (descartado: no evita la regla de Android 15 para `camera` y
  añade un paquete).

## 012 — Sin copia de seguridad de Android, y un vigilante por alarma que reviva el servicio
- Contexto: el 2026-10-23 a las 07:28 UTC, en pleno juez de 24 h, el proceso murió sin `am_kill`: el registro de
  eventos muestra `full_backup_package com.vigiahud.app` justo antes de `am_proc_died`. Android hace copias de
  seguridad automáticas de las apps con `allowBackup="true"` (el valor de la plantilla MAUI) cuando el móvil está
  cargando, inactivo y con wifi, y para ello mata el proceso. Además esa copia sube los ficheros privados de la app
  (registro y fotos del interior de la casa) a la cuenta de Google. Un segundo después Android programó el reinicio
  sticky (`am_schedule_service_restart`), pero con la app inactiva y sin su notificación aplicó
  `am_stop_idle_service` antes de que el proceso nuevo ejecutara el servicio; el proceso quedó vacío y fue matado a
  los 23 s. La vigilancia estuvo muerta 15 minutos, hasta que el dueño abrió la app. Reproducido con `am crash`.
- Decisión: (1) `android:allowBackup="false"`: nada de la app sale del móvil por esa vía y el sistema no lo mata
  para copiarlo. (2) `START_STICKY` se mantiene, pero no se confía en él: `WatchService` programa con `AlarmManager`
  (`setAndAllowWhileIdle`, inexacta, cada 10 min) un `WatchdogReceiver` que, si el servicio no está corriendo y la
  vigilancia estaba activa (`IWatchSettings.Enabled`), lo arranca y lo apunta en el registro (`info watchdog: service
  start requested`); el receptor vuelve a programarse a sí mismo, de modo que sobrevive a la muerte del proceso. El
  servicio cancela la alarma al pararse a propósito. En Android 10 un receptor de alarma puede arrancar un servicio
  en primer plano; en Android 12+ haría falta una alarma exacta (`SCHEDULE_EXACT_ALARM`) para la exención, cosa que
  no se valida en esta fase.
- Alternativas: `JobScheduler` periódico (descartado: mínimo 15 min y sin garantía de puntualidad en reposo);
  dos procesos que se vigilen mutuamente (descartado: ColorOS los mata juntos); confiar en el sticky (descartado:
  demostrado que falla).
