# VigíaHud

Un móvil Android viejo convertido en cámara de vigilancia para una segunda residencia.
Detecta personas, avisa por Telegram con fotos y responde a comandos. Sin servidor propio.
Hermano de CieloHud, VientoHud y GincanaHud (mismo autor).

Lee `docs/VISION.md` para el porqué y `docs/PLAN.md` para las fases. El estado actual está en `docs/STATUS.md`.

## Principios (no negociables)

- **Sobrevivir es lo primero.** Un vigilante que Android cierra sin avisar es peor que no tener ninguno.
  Cualquier cambio que ponga en riesgo que el servicio siga vivo durante días se rechaza.
- **El silencio no es una buena noticia.** Siempre debe haber un latido periódico; si falta, el usuario sabe que algo falla.
- **Sin backend propio.** Avisos y comandos por la Bot API de Telegram con long polling (`getUpdates`), sin webhooks
  ni servidores. Imágenes guardadas solo en el móvil (con rotación) y en el chat de Telegram del usuario.
- **Solo obedece a su dueño.** El bot ignora cualquier mensaje que no venga del chat ID autorizado.
- **Secretos fuera del repo.** Token del bot y chat ID nunca en el código ni en git.
- **El juez son los registros y las grabaciones.** Uptime medido, no supuesto. Detección validada contra secuencias
  de imágenes etiquetadas: falsas alarmas y detecciones perdidas, con números.
- **Solo hacia dentro.** La cámara vigila el interior de la propiedad; no se diseña nada para grabar la vía pública.
- **Fase a fase.** Trabaja SOLO en la fase actual de `docs/PLAN.md`. No adelantes trabajo de fases futuras.

## Stack

- .NET 10, C#.
- `VigiaHud.Core`: lógica sin dependencias de plataforma (detección de movimiento sobre frames, máquina de estados
  armado/desarmado, cooldown de avisos, programación del latido, parseo de comandos).
- `VigiaHud.App`: .NET MAUI solo Android (mínimo Android 10, `docs/DECISIONS.md` 003). Cámara, servicio en primer plano,
  batería, red. Solo orquesta lo que dice Core. `dotnet build src/VigiaHud.App -t:Install` la instala en el móvil por `adb`.
- `VigiaHud.Console`: herramienta para pasar la detección sobre secuencias de imágenes grabadas (el juez offline). Llega en la fase 3.
- `VigiaHud.Core.Tests`: xUnit.
- Mismo esquema de repositorio y misma estética que CieloHud y VientoHud (`docs/DECISIONS.md` 005): ante la duda,
  mira cómo lo hace VientoHud.

## Convenciones

- Código, identificadores y comentarios en inglés. Documentación en `docs/` en español.
- Tiempo interno en UTC (`DateTimeOffset`); hora local solo al mostrar o en mensajes al usuario.
- Mensajes al usuario (Telegram) en español, cortos y claros.
- Todo lo de plataforma (cámara, batería, red, Telegram) detrás de interfaces, con implementaciones falsas para tests.
- Registro local persistente (fichero con rotación) de arranques, paradas, errores, latidos y avisos.

## Forma de trabajar

- Antes de añadir un paquete NuGet o un modelo de ML, explica por qué y qué alternativas has valorado
  (licencia, tamaño, rendimiento en un móvil antiguo, mantenimiento). Verifica en la fuente; no lo supongas.
- Para todo lo que dependa de la versión de Android (servicios en primer plano, permisos de cámara, optimización
  de batería), comprueba la documentación oficial actual y documenta en DECISIONS.md qué versiones soportamos.
- Tras cada cambio: `dotnet build` y `dotnet test`. No des un paso por terminado si algo falla.
- Al terminar un paso, actualiza `docs/STATUS.md` (qué funciona, qué falta, cómo se validó).
- Las decisiones relevantes van a `docs/DECISIONS.md`: contexto, decisión, alternativas.
- Pasos pequeños y verificables. Si una tarea es grande, propón cómo dividirla antes de empezar.
- El repo está en GitHub (`victorvalenciamunoz/VigiaHud`) y `main` está protegida: cada paso va en una rama y entra
  por un pull request con el CI (`Tests de Core`) en verde. Nada de commits directos a `main`.
- Las pruebas en el móvil se documentan en `docs/verificacion/` (capturas, registros extraídos con `adb`), con la fecha en el nombre.
