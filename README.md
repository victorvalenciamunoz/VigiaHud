<p align="center">
  <img src="src/VigiaHud.App/Resources/AppIcon/appiconfg.svg" width="112" alt="Icono de VigíaHud: un ojo en cian" />
</p>

<h1 align="center">VigíaHud</h1>

<p align="center">
  <b>Un móvil Android viejo convertido en cámara de vigilancia para la segunda residencia.</b><br/>
  Vigila el interior con la pantalla apagada, detecta personas y te lo cuenta por Telegram con fotos. Sin servidor.
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" />
  <img alt=".NET MAUI Android" src="https://img.shields.io/badge/MAUI-Android-3DDC84?logo=android&logoColor=white" />
  <a href="https://github.com/victorvalenciamunoz/VigiaHud/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/victorvalenciamunoz/VigiaHud/actions/workflows/ci.yml/badge.svg" /></a>
  <img alt="Sin backend" src="https://img.shields.io/badge/backend-ninguno-0B1218" />
  <a href="LICENSE"><img alt="Licencia MIT" src="https://img.shields.io/badge/licencia-MIT-4DD2FF" /></a>
</p>

---

## Qué es

La segunda residencia tiene alarma y wifi, pero no cámara. Un móvil Android viejo, enchufado y conectado al wifi,
vigila el interior con la pantalla apagada. Desde tu móvil hablas con él por Telegram:

1. **Detecta una persona** y te manda una ráfaga de fotos al momento.
2. **`/foto`, `/estado`, `/armar`, `/desarmar`**: una imagen del momento, batería y temperatura, y que no te avise de ti mismo.
3. **"Sigo vivo" cada día.** Si no llega, algo falla. Y aviso si se va la luz: el móvil sigue con su batería.

Sin servidor propio: el móvil habla con la Bot API de Telegram por *long polling*, y solo obedece al chat autorizado.
Toda la detección se hace en el móvil; las únicas imágenes que salen son las que te manda a tu Telegram.
Vigila solo el interior de la propiedad.

Hermano de [CieloHud](https://github.com/victorvalenciamunoz/CieloHud) y
[VientoHud](https://github.com/victorvalenciamunoz/VientoHud): misma estética, mismos principios.

## Estructura

| Proyecto | Qué es |
|---|---|
| `src/VigiaHud.Core` | La lógica, sin plataforma: detección de movimiento sobre frames, armado, cooldown de avisos, latido, comandos. |
| `src/VigiaHud.App` | La app .NET MAUI para Android: cámara, servicio en primer plano, batería, red. Solo orquesta lo que dice Core. |
| `tests/VigiaHud.Core.Tests` | xUnit, con implementaciones falsas de todo lo de plataforma. |
| `docs/` | [Visión](docs/VISION.md), [plan por fases](docs/PLAN.md), [estado](docs/STATUS.md) y [decisiones](docs/DECISIONS.md), en español. |

`src/VigiaHud.Console`, el juez offline que pasa la detección sobre secuencias grabadas, llega en la fase 3.

## Compilar y probar

Hace falta el SDK de .NET 10 (`global.json` fija la versión) y, para la app, la carga de trabajo de Android.

```bash
dotnet build                      # Core, tests y app (la app tarda un poco más)
dotnet test                       # los tests de Core
dotnet build src/VigiaHud.App     # solo la app Android
```

El token del bot y el chat ID nunca van en el repo: se configuran en el propio móvil (fase 2).

## El móvil

Se desarrolla y valida en un OPPO A5 2020 (Android 10, ColorOS 7.1). Mínimo Android 10; versiones más nuevas deberían
funcionar, pero no están validadas. Los detalles y el porqué, en [`docs/DECISIONS.md`](docs/DECISIONS.md).

## Estado

Fase 1 de 5, que sobreviva: un servicio en primer plano que hace fotos con la pantalla apagada y sigue vivo durante días.
Lo hecho y lo que falta, paso a paso y con cómo se validó, en [`docs/STATUS.md`](docs/STATUS.md).

## Licencia

MIT, ver [`LICENSE`](LICENSE). Las fuentes de terceros tienen las suyas: [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
