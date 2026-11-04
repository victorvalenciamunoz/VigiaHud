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

## 013 — Recuperar la cámara en cuanto quede libre, con la CPU sujeta mientras tanto
- Contexto: el juez de 72 h (2026-10-27) falló por un solo incidente: el 2026-10-26 a las 21:02 UTC la app de
  cámara de OPPO echó a VigíaHud de la cámara (se abrió al coger el móvil; la usó 3 s) y la vigilancia tardó 15 min
  en recuperarla, aunque el código reabría a los 30 s. La historia de batería de Android (`dumpsys batterystats
  --history`) muestra por qué: 10 s después de apagarse la pantalla no había ningún wake lock activo y la CPU entró
  en sueño profundo. El wake lock del servicio no estaba registrado en el sistema (`dumpsys power` tampoco lo
  mostraba al día siguiente, con el servicio vivo): algo lo quitó, probablemente ColorOS, con la app sin la exclusión
  de batería desde el reinicio del 24. Durante tres días no se notó porque la cámara en marcha mantiene el móvil
  despierto por sí sola. Sin cámara, los temporizadores de `Handler` (tiempo de CPU despierta) dejaron de avanzar:
  la reapertura y las líneas `capture` solo progresaban en los despertares de la alarma del vigilante.
- Decisión:
  - `CameraCapturer` se suscribe a `CameraManager.AvailabilityCallback` y reabre en cuanto el servicio de cámara
    avisa de que la cámara está libre; los 30 s quedan como respaldo. Al perderla toma un wake lock aparte
    (`VigiaHud::CameraRecovery`, 2 min) que suelta al volver a tener el flujo. Que otra app tenga la cámara al
    intentar abrir (`CAMERA_IN_USE`) es `info` ("camera busy with another app; waiting"), no `error`: solo la
    expulsión cuenta como error en el registro.
  - El wake lock principal se toma con límite (15 min) y se vuelve a pedir al sistema cada minuto, con cada línea
    `capture`. Un `acquire` no contado llega siempre al sistema, así que lo recrea si alguien lo quitó.
  - El vigilante por alarma (cada 10 min, la única forma de despertar una CPU dormida) también renueva el wake lock
    y, si la cámara sigue perdida, la reabre en el acto.
- Validado por `adb` (2026-10-27): con la app de cámara de OPPO abierta 4 s, la vigilancia recupera la cámara 0,2 s
  después de que la suelte (antes, 30 s). El sueño profundo no se puede provocar con el cable USB conectado
  (`dumpsys battery unplug` y `deviceidle force-idle` no bastan: el USB mantiene la CPU despierta); esa parte se
  prueba desenchufando de verdad.
- Alternativas: reabrir con una alarma exacta (`setExactAndAllowWhileIdle`) en vez de con el aviso del servicio de
  cámara (descartado como vía principal: en Doze se retrasa hasta 9 min; queda el vigilante como red);
  `FLAG_KEEP_SCREEN_ON` (plan B de `PLAN.md`; descartado mientras esto funcione).

## 014 — Sin modo ahorro: la cámara siempre en marcha también a batería
- Contexto: medido el 2026-10-28 y 29, la vigilancia gasta 8-9 % de batería por hora (unos 320 mA de media; Android
  atribuye casi el 90 % del gasto a la cámara de VigíaHud). Desde el 100 % aguanta unas 11-12 h sin cargador. La
  batería del OPPO está sana (5000 mAh declarados, descarga real coherente).
- Decisión (del autor, 2026-10-29): se deja así. El móvil vive enchufado y la batería solo cuenta en un corte de luz;
  la fase 2 avisará por Telegram al perder el cargador, así que el dueño sabrá que quedan unas horas. No se añade un
  modo ahorro al plan.
- Alternativas: al perder el cargador, encender la cámara a ratos en vez de tenerla siempre en marcha (descartado:
  multiplicaría la autonomía pero sin flujo de cámara ColorOS duerme el móvil, los temporizadores se paran y las
  alarmas se retrasan hasta 9 min en Doze, justo lo que tumbó el primer juez de 72 h; además vigilaría peor).

## 015 — La fase 2 avanza en paralelo al juez de la fase 1, solo en Core y tests
- Contexto: la fase 1 tiene todo el código hecho y solo espera el veredicto del juez de 72 h, que necesita tres días
  sin reinstalar la app en el móvil. `PLAN.md` dice trabajar solo en la fase actual.
- Decisión (del autor, 2026-10-29): empezar la fase 2 sin tocar el móvil, en Core y con tests, mientras corre el
  juez. Nada de la fase 2 se instala hasta que el juez pase; si falla, se arregla primero la fase 1. La fase 2 se
  divide en cinco pasos: 1 comandos y dueño; 2 cliente de Telegram, probado con respuestas reales grabadas; 3
  reconexión con espera creciente; 4 latido diario y avisos de cargador y de exclusión de batería; 5 en el móvil, con
  el juez de la fase 2. El latido diario sale a las **09:00 hora local**: por la mañana se confirma que ha pasado bien
  la noche, que es cuando nadie mira.
- Alternativas: esperar al veredicto y empezar la fase 2 con la 1 cerrada (descartado: tres días parados sin ganar
  nada, porque lo de Core no puede romper el juez); latido a las 21:00 o a las 08:00.

## 016 — Cliente de Telegram propio con HttpClient, sin paquete
- Contexto: la fase 2 solo usa tres llamadas de la Bot API: `getUpdates` (long polling), `sendMessage` y
  `sendPhoto`. Comprobado en la fuente el 2026-10-29: la Bot API (versión 10.3) acepta HTTPS con
  `https://api.telegram.org/bot<token>/MÉTODO`, JSON o `multipart/form-data` para subir ficheros, y siempre responde
  con un objeto con `ok` y `result` o `description` y `error_code`.
- Decisión (del autor, 2026-10-29): un cliente propio en Core con el `HttpClient` de .NET y `System.Text.Json`,
  detrás de una interfaz con implementación falsa para los tests y respuestas reales grabadas como fixtures (sin token
  ni chat ID).
- Alternativas: el paquete `Telegram.Bot` 22.10.3.3 (MIT, 10,8 millones de descargas, mantenido, Bot API 10.3;
  descartado: 3 MB y tres dependencias para usar tres llamadas, en un móvil con poco espacio, y menos control de los
  tiempos de espera del long polling, que importan para reconectar bien).

## 017 — Secretos de Telegram en el PC y fixtures grabadas sin datos personales
- Contexto: el paso 2 de la fase 2 necesita el token del bot y el chat ID del dueño en el PC para grabar respuestas
  reales de la Bot API, y el repo es público (CLAUDE.md: "Token del bot y chat ID nunca en el código ni en git").
- Decisión: los dos van en los secretos de usuario de .NET con id `vigiahud`
  (`dotnet user-secrets set "Telegram:Token" (Read-Host 'Token') --id vigiahud`), un JSON en el perfil de Windows,
  fuera del repo; el token se teclea en la pregunta, no en el comando, para que no quede en el historial. El script
  `scripts/record-telegram-fixtures.cs` lee el token de ahí, graba seis respuestas reales (mensajes, sondeo vacío,
  texto y foto enviados, 401 y 409), sustituye el chat y el nombre del dueño, el ID, el nombre y el usuario del bot y
  los identificadores de fichero por valores inventados, se niega a guardar un fichero si después queda algún dato
  real y guarda el chat ID en `Telegram:ChatId` sin mostrarlo. El error 429 no se graba: provocarlo exigiría saturar
  el bot; se prueba con la forma que da la documentación.
- Alternativas: variables de entorno de usuario (la primera propuesta; el autor prefirió los secretos de usuario);
  grabar a mano con curl (descartado: el token quedaría en el historial y la limpieza
  dependería de no olvidarse de nada).

## 018 — Reconexión con un bucle propio que no se rinde nunca
- Contexto: el paso 3 de la fase 2 pide reconectar solo si cae el wifi o Telegram no responde, registrando cada
  corte, y el juez de la fase 2 corta el wifi 10 min y espera que el bot vuelva y avise. Valorado el 2026-10-29 con las
  fuentes: Polly.Core 8.9.0 (BSD-3, sin dependencias en .NET 10) y Microsoft.Extensions.Http.Resilience 10.11.0 (MIT,
  encima de Polly, con tres dependencias más). La configuración estándar de esta última corta cada intento a los 10 s
  y la petición a los 30 s (el long polling espera 50 s), reintenta 3 veces y se rinde, y reintenta también los
  envíos, que duplicarían un `/estado`.
- Decisión (del autor, 2026-10-29): un bucle propio en Core, `TelegramPoller`, sin paquetes. No se rinde nunca: espera
  2 s y dobla hasta 5 min, sin azar (es un solo cliente); respeta `retry_after`; un token rechazado (401, 403, 404) o
  un error inesperado esperan 5 min, porque esperar no los arregla. Un mensaje se da por recibido solo cuando está
  contestado: si contestar falla por la red vuelve a llegar; si falla por un fallo nuestro se salta y se apunta, para
  no atascar la cola. Cada corte es una línea `info` al empezar y otra al acabar, con su duración y los intentos, y se
  entrega al llamador para avisar al dueño. Un corte de red es `info`, no `error`: es el tiempo que hace, no un fallo
  de la app, y no debe ensuciar la cuenta de errores del juez de la fase 1. El token rechazado sí es `error`.
- Alternativas: Polly.Core (encaja mejor que la de Microsoft, pero solo ahorra la parte fácil; el registro de cortes
  es código nuestro igualmente); Microsoft.Extensions.Http.Resilience (habría que desactivar casi toda su
  configuración estándar y meter inyección de dependencias de `HttpClient`).

## 019 — Los mensajes que el bot manda por su cuenta
- Contexto: paso 4 de la fase 2. `PLAN.md` pide el latido diario con resumen y los avisos de cargador; en un corte de
  luz el router también se apaga, así que el móvil sigue vigilando con su batería pero sin internet: los avisos solo
  pueden salir cuando vuelve la corriente, y lo que avisa a tiempo es que falte el latido de las 09:00. (Arreglos
  posibles fuera del código, sin decidir: un SAI para el router o una SIM con datos en el móvil.)
- Decisiones del autor (2026-10-29):
  - Aviso de corriente perdida a los **10 min** sin cargador, con el **margen aproximado** de batería (al 9 % por
    hora medido, DECISIONS 014); los cortes más cortos no avisan pero cuentan en el resumen del latido.
  - Un aviso de **batería baja al 15 %** cuando está sin cargador.
  - El latido de las 09:00 que no pudo salir se envía **con retraso**, diciéndolo.
  - Al volver la conexión con Telegram solo se avisa de los cortes de **5 min** o más (el juez de la fase 2 corta 10);
    los demás van al registro y al resumen del latido.
- Decisiones técnicas: lo que el bot dice por su cuenta pasa por una cola (`Outbox`) en memoria, en orden y con un
  máximo de 50; si un mensaje sale más de 2 min tarde, dice de cuándo es. Si el móvil muere con mensajes en la cola,
  se pierden, pero el registro guarda lo ocurrido. El reloj del bot se despierta al menos cada minuto y vuelve a
  mirar la hora real: un temporizador puede no contar mientras la CPU duerme, la hora real siempre avanza. Las
  respuestas a comandos no pasan por la cola: salen directamente y, si fallan por la red, el comando vuelve a llegar
  (DECISIONS 018). Lo único que debe sobrevivir a un reinicio en esta parte es la hora del último latido.
- Alternativas: avisar del cargador al momento o a los 2 min, latido solo a su hora o saltarlo, sin margen estimado,
  avisar de todos los cortes de conexión o solo de los de 10 min o más (todas valoradas con el autor); guardar la
  cola en disco (descartado por ahora: más código y escrituras para un caso raro que el registro ya cubre).

## 020 — Los avisos de corriente salen de mirar el móvil cada minuto
- Contexto: la parte 4.2 de la fase 2 (avisos de corriente, batería baja y exclusión de batería, DECISIONS 019).
  Android avisa al conectar y desconectar el cargador (`ACTION_POWER_CONNECTED` y `ACTION_POWER_DISCONNECTED`), pero
  desde Android 8 esos avisos solo llegan a quien se suscribe mientras está en marcha, no por el manifiesto.
- Decisión: `PowerWatch` en Core decide a partir de lecturas del estado del móvil que el reloj de `BotRunner` toma
  al menos cada minuto (la misma lectura de batería del latido, `ACTION_BATTERY_CHANGED`). Un aviso puede salir hasta
  un minuto tarde, que con un umbral de 10 min no importa, y no hace falta ningún receptor más en la app. El margen
  de batería es el porcentaje entre el 9 % por hora medido, redondeado a horas ("unas 9 h", "1 h aproximadamente",
  "menos de 1 h"): un modelo simple a propósito, que puede fallar en una hora o dos. Un corte de corriente va del
  primer minuto que se ve sin cargador al primero que se ve con él.
- Alternativas: suscribirse a los avisos de cargador de Android (descartado: más código de plataforma para ganar
  como mucho un minuto en un aviso que espera 10); estimar el margen con la descarga reciente medida en el propio
  móvil (descartado por ahora: más complejo y la cifra fija ya da el orden de magnitud).

## 021 — El bot en el móvil: secretos por adb y foto grande bajo demanda
- Contexto: paso 5 de la fase 2. El token y el chat ID no pueden ir en git ni en el APK; el móvil viejo no tiene
  Telegram ni es cómodo teclear en él. El fotograma de la vigilancia mide 320×240, poco para echar un vistazo.
- Decisiones del autor (2026-11-03): los secretos llegan **desde el PC por adb**; `/foto` da una foto de **1280×960**.
- Decisiones técnicas:
  - `scripts/push-telegram-secrets.ps1` lee los secretos de usuario del PC (DECISIONS 017) y los escribe por la
    entrada de `adb shell run-as` en `files/telegram.json`, con permisos 600, sin fichero temporal y sin mostrarlos.
    Solo funciona con la app instalada desde el PC (depurable), que es como se instala. El fichero queda en el
    almacenamiento privado de la app y no sale del móvil: la copia de seguridad de Android está desactivada
    (DECISIONS 012). `WatchService` lo lee al arrancar; sin él, la vigilancia sigue sola y lo apunta.
  - La foto grande es una captura suelta (`StillCapture`) en la sesión de cámara que ya está en marcha, hacia una
    segunda salida JPEG que el flujo de vigilancia no usa: la exposición ya está ajustada (DECISIONS 008), no hay
    flash, y la orientación la escribe la cámara en el JPEG (90° con el móvil de pie, por el montaje del sensor del
    OPPO). Si no llega en 5 s, se envía el último fotograma de la vigilancia; sin fotograma reciente, "la cámara no
    está lista".
  - La conversión de cada fotograma a JPEG copia cada plano de color de una vez en vez de leerlo píxel a píxel a través
    de Java: la app baja del 51 % al 22 % de un núcleo. El servicio de cámara del sistema sube unos 10 puntos (del 94
    al 101-106 %), quizá por la segunda salida; en conjunto se gasta menos. Pendiente de medir en una descarga.
  - Cada respuesta al dueño deja una línea en el registro con lo que se pidió, nunca con sus palabras.
- Alternativas: escribir los secretos en una pantalla de la app (descartado por el autor: teclear 46 caracteres en el
  móvil viejo); meterlos en el APK al compilar (descartado: el token viajaría dentro del fichero instalable); subir
  todo el flujo de vigilancia a 1280×960 (descartado: más consumo continuo para una foto que se pide de vez en
  cuando); abrir una sesión nueva solo para la foto (descartado: la exposición tendría que volver a ajustarse y las
  fotos sueltas sin flujo salían negras, DECISIONS 008).

## 022 — Las fotos se ponen derechas solas, con el acelerómetro
- Contexto: el sensor de la cámara del OPPO está montado a 90°; una foto sale derecha solo si se le escribe el giro
  que corresponde a cómo está el móvil. El autor todavía no sabe si lo colocará de pie o tumbado, y tumbado puede
  quedar hacia cualquiera de los dos lados.
- Decisión (del autor, 2026-11-03): orientación automática. En cada foto la app lee una vez el acelerómetro, saca la
  rotación del móvil al cuarto de vuelta más cercano (la regla de `OrientationEventListener` de Android: plano si la
  inclinación no llega a unos 27°) y escribe en el JPEG el montaje del sensor más esa rotación
  (`PhotoOrientation` en Core, `GravityReader` en la app). Plano, o sin lectura en 1 s, cuenta la última posición
  conocida y, antes de ninguna, de pie. El sensor solo se escucha durante ese momento: no gasta mientras vigila. La
  foto de respaldo (el fotograma de 320×240) no se gira.
- Alternativas: un giro fijo para el móvil de pie o tumbado (descartado: hay que acertar el lado y deja de valer si
  se recoloca); un ajuste en la app (descartado: una decisión más para el dueño sin ganar nada).

## 023 — Cualquier fallo de red es "sin red", y se reintenta en cuanto Android avisa
- Contexto: juez de la fase 2 (2026-11-03). En el OPPO, el componente de red nativo de Android que usa `HttpClient`
  lanza `WebException` al perder la red, no `HttpRequestException` ni `IOException`, los dos que el cliente
  reconocía: el bucle lo trató como error inesperado, lo apuntó como `error` y esperó 5 min de golpe. Además, tras
  un corte largo la espera creciente llega a 5 min, y el bot tardó 3,5 min en notar que el wifi había vuelto.
- Decisión:
  - `TelegramClient`: cualquier excepción al enviar o leer la petición, salvo una cancelación pedida por quien llama,
    es `TelegramUnavailableException` con el nombre del tipo y el token oculto. Los errores inesperados del bucle
    quedan para lo que no sea hablar con Telegram.
  - `TelegramPoller.RetryNow` corta la espera en curso; `NetworkWatcher` en la app lo llama cuando Android avisa de
    que hay una red disponible (`ConnectivityManager.registerDefaultNetworkCallback`, API 24+; permiso
    `ACCESS_NETWORK_STATE`, que ya estaba). Si el reintento falla, la espera sigue creciendo donde iba: una red sin
    internet no puede hacer que el bot reintente sin parar. (Decisión del autor: reintentar al volver la red.)
- Alternativas: enumerar los tipos de error de cada plataforma (descartado: el siguiente que no estuviera en la lista
  volvería a tratarse como inesperado); bajar el máximo de espera de 5 min (descartado: más intentos inútiles durante
  cortes largos sin resolver el caso del wifi que vuelve).

## 024 — Plan de la fase 3 y grabaciones de prueba
- Contexto: la fase 3 pide detectar movimiento en el móvil y juzgarlo contra grabaciones etiquetadas, con una tabla
  de falsas alarmas y detecciones perdidas. Las grabaciones son el interior de una casa y el repo es público. La
  vigilancia gasta 5,3 % de batería por hora y la cámara del sistema casi un núcleo (DECISIONS 021); la cámara da 15
  fotogramas por segundo a 320×240 (su mínimo) y la vigilancia usa uno cada 2 s.
- Decisiones del autor (2026-11-04):
  - **Seis pasos**: 1 grabar y traer grabaciones; 2 consola para mirarlas y etiquetarlas (`info`, `hoja`); 3 detector
    y juez en el PC (`juzgar`, `barrer`), sin tocar el móvil; 4 detector en el móvil; 5 editor de máscaras; 6 juez
    final con un lote grabado después de fijar los umbrales, para no juzgar con lo mismo con que se ajusta.
  - Las grabaciones viven **fuera del repo**, en `%USERPROFILE%\.vigiahud\secuencias\`; en el repo solo una lista con
    nombres neutros, etiquetas y la huella (SHA-256) de cada fichero.
  - Se graban con **`/grabar` desde Telegram**, solo para fabricar escenas de prueba: la vigilancia nunca graba.
  - En esta fase, al detectar movimiento, **solo registro, foto guardada en el móvil y recuento en el latido**; el
    aviso por Telegram llega en la fase 4, cuando se confirme que es una persona (PLAN.md).
  - El **lote 1** (ajuste) se graba donde está ahora el móvil; el **lote del juez**, en la segunda residencia.
  - Criterio del paso 4: como mucho **5,8 % de batería por hora** y la app por debajo del **25 % de un núcleo**.
  - El margen de los avisos de batería pasa del 9 % al **5,3 % por hora** medido en la fase 2 (DECISIONS 020); se
    revisa con la medida del paso 4.
  - El dibujo del editor de máscaras se confirma al llegar al paso 5. Para la fase 5 queda la idea de guardar unos
    segundos alrededor de cada detección, para añadir las falsas alarmas reales a las pruebas.
- Decisiones técnicas:
  - Una grabación guarda **un fotograma por segundo** (la consola puede probar también uno cada 2 s): la imagen gris
    a 160×120 (media de bloques de 2×2 del plano Y), exactamente la que verá el detector, comprimida sin pérdida
    (Deflate), y el JPEG en color de 320×240 de ese momento, para etiquetar y para la fase 4. Un fichero `.vseq` por
    grabación: cabecera JSON y un registro por fotograma escrito entero y a disco, de modo que si Android mata la app
    se pierde como mucho el último fotograma. Sin paquetes: `System.IO.Compression` y `System.Text.Json`.
  - En el móvil van en la carpeta privada de la app (`files/recordings`), no en la galería, para que ninguna nube las
    suba. De 10 s a 12 h; una a la vez; se para sola con menos de 1,5 GB libres (había 4,8 GB, DECISIONS 003).
  - Medido en el OPPO (2026-11-04): 89 MB por hora (14,3 KB de imagen gris y 9,9 KB de JPEG por fotograma), unos
    1,1 GB por noche; la app sube unos 4 puntos de CPU mientras graba y nada cuando no graba.
- Alternativas: dentro del repo excluidas por `.gitignore` (un `git add` despistado las publicaría) o en un repo
  privado de GitHub (servidor ajeno y cupo de ficheros grandes); grabar desde el PC por `adb` (exige el PC al lado);
  avisar del movimiento ya por Telegram (avisaría del dueño y de luces y sombras hasta la fase 5); grabar el vídeo
  normal de la cámara (cambia los píxeles y no deja grabar mientras se vigila); guardar el fotograma entero en color
  sin pérdida (unos 3 GB por noche, más de lo que cabe).

## 025 — Etiquetas en texto junto a cada grabación, y una hoja para marcarlas tocando
- Contexto: paso 2 de la fase 3. El juez necesita saber en qué segundos de cada grabación hay alguien; las
  grabaciones viven fuera del repo (DECISIONS 024) y el autor las tiene que mirar para etiquetarlas. Comprobado que
  en el PC hay navegador y Node, pero no Python.
- Decisión:
  - Etiquetas: un `.txt` con el mismo nombre que la grabación, una línea `persona mm:ss-mm:ss` (u `h:mm:ss`) por
    tramo con alguien, contando desde el inicio de la grabación; los dos extremos son fotos y entran las dos
    (`PersonInterval.Contains`: del segundo inicial hasta antes del siguiente al final). `#` para comentarios. Sin
    fichero, la grabación está sin etiquetar y el juez no la usa; un fichero sin líneas `persona` dice "sin nadie".
    `SequenceLabels` en Core, con los errores en español y el número de línea, porque los lee el autor.
  - `VigiaHud.Console` (ejecutable `vigia`, sin paquetes: dos comandos no piden System.CommandLine):
    - `info` saca una fila Markdown por grabación (`SequenceSummary` en Core: fotos, duración, hueco máximo, si está
      cortada, tamaño y SHA-256). Esa tabla es la lista del repo, `docs/secuencias.md`.
    - `hoja` escribe junto a la grabación una página HTML autocontenida (las fotos dentro, nada de la red) con la
      paleta de la familia. Tocar una foto y otra marca un tramo en ámbar y genera las líneas para COPIAR. Las fotos
      se giran según el montaje del sensor (de pie), con un botón GIRAR. Como mucho 600 fotos repartidas, para que una
      noche se abra; `--desde`/`--hasta` para mirar un rato foto a foto y `--cada N` para elegir la separación.
      El dibujo lo confirmó el autor antes de programarlo.
  - El CI compila también la consola (no necesita MAUI).
- Alternativas: etiquetas en JSON (descartado: el autor las escribe o pega a mano, y una línea por tramo se lee sola);
  dentro de la cabecera de la grabación (descartado: habría que reescribir un fichero de 1 GB para corregir una
  etiqueta, y la huella cambiaría); una herramienta de etiquetado de escritorio o un paquete de HTML (descartado: una
  página con 60 líneas de JavaScript basta).
