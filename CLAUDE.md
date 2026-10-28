# VigíaHud

Un móvil Android viejo convertido en cámara de vigilancia para una segunda residencia.
Detecta personas, avisa por Telegram con fotos y responde a comandos. Sin servidor propio.
Hermano de CieloHud, VientoHud y GincanaHud (mismo autor).

Lee `docs/VISION.md` para el porqué y `docs/PLAN.md` para las fases. El estado actual está en `docs/STATUS.md`.

Las normas comunes a los proyectos del autor (idioma, pasos, build y test, STATUS y DECISIONS, ramas y PR) están en
sus instrucciones personales (`~/.claude/CLAUDE.md`), fuera del repo. Aquí va solo lo propio de VigíaHud, que es una
app MAUI:

@~/.claude/tipos/maui.md

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

## Stack

- .NET 10, C#.
- `VigiaHud.Core`: lógica sin dependencias de plataforma (detección de movimiento sobre frames, máquina de estados
  armado/desarmado, cooldown de avisos, programación del latido, parseo de comandos).
- `VigiaHud.App`: .NET MAUI solo Android (mínimo Android 10, `docs/DECISIONS.md` 003). Cámara, servicio en primer plano,
  batería, red. `dotnet build src/VigiaHud.App -t:Install` la instala en el móvil por `adb`.
- `VigiaHud.Console`: herramienta para pasar la detección sobre secuencias de imágenes grabadas (el juez offline). Llega en la fase 3.
- `VigiaHud.Core.Tests`: xUnit.

## Convenciones

- Mensajes al usuario (Telegram) en español, cortos y claros; la hora, en local.
- Telegram también va detrás de una interfaz, con implementación falsa para tests.
- Registro local persistente (fichero con rotación) de arranques, paradas, errores, latidos y avisos.
- Un modelo de ML nuevo se justifica como un paquete y además por su tamaño y su rendimiento en un móvil antiguo.
- Las pruebas en el móvil se documentan en `docs/verificacion/` (capturas, registros extraídos con `adb`), con la fecha en el nombre.

## Repo

`victorvalenciamunoz/VigiaHud`, con `main` protegida; el CI que tiene que estar en verde es `Tests de Core`.
