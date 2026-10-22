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
