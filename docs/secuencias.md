# Grabaciones de prueba

Las escenas con las que se ajusta y se juzga el detector de movimiento (fase 3, DECISIONS 024 y 025). Son el interior
de una casa: **las grabaciones no entran nunca en el repo**. Viven en `%USERPROFILE%\.vigiahud\secuencias\`, cada una
con su fichero de etiquetas al lado. Aquí solo va la lista, con la huella de cada fichero para saber sobre qué
grabaciones exactas se hizo cada tabla.

## Cómo se graba y se etiqueta

1. Desde Telegram: `/grabar 2 nombre` (2 minutos) o `/grabar 12h nombre`. Nombres neutros: qué pasa, nunca dónde.
2. En el PC, con el móvil por USB: `powershell -File scripts\pull-recordings.ps1`.
3. `dotnet run --project src/VigiaHud.Console -- hoja nombre` y abrir la página que dice, en el navegador.
4. Tocar la primera y la última foto de cada tramo con alguien, COPIAR, y pegarlo en el `.txt` con el mismo nombre que
   la grabación (lo dice la página). Si no sale nadie, el fichero lleva solo un comentario: así cuenta como "sin nadie".
   Sin `.txt`, la grabación está sin etiquetar y el juez no la usa.
5. `dotnet run --project src/VigiaHud.Console -- info` y copiar aquí la tabla.

## Lote 1 (ajuste, en casa del autor)

Para elegir los umbrales del detector en el paso 3. De 1 a 3 minutos cada una, salvo la noche. En las escenas sin
persona hay que quedarse fuera de la imagen (si se sale, se etiqueta).

| Nombre | Qué se hace |
|---|---|
| `persona-cruza` | Cruzar la habitación de lado a lado, a paso normal. Dos o tres veces. |
| `persona-quieta` | Entrar, quedarse quieto unos 30 s y salir. |
| `persona-lenta` | Cruzar muy despacio. |
| `persona-lejos` | Pasar por el fondo, lo más lejos posible de la cámara. |
| `persona-hacia` | Acercarse de frente a la cámara y alejarse. |
| `persona-poca-luz` | Cruzar con solo una lámpara o una luz débil. |
| `persona-noche` | Cruzar de noche con las luces apagadas (para saber cuánto ve; el arreglo es de la fase 5). |
| `luz-techo` | Sin nadie a la vista: encender y apagar la luz del techo varias veces. |
| `luz-lampara` | Sin nadie a la vista: encender y apagar una lámpara. |
| `cortina` | Sin nadie a la vista: una cortina o persiana que se mueve (ventana abierta o a mano desde fuera de la imagen). |
| `sol` | Una o dos horas de tarde con sol entrando y sombras que se mueven (`/grabar 2h sol`). |
| `tele` | La tele encendida, si se ve desde la cámara. |
| `casa-vacia` | Una noche entera sin nadie (`/grabar 12h casa-vacia`); unos 1,1 GB: traerla al PC por la mañana. |

## Lista

Todavía ninguna (la tabla la saca `info`). Las primeras pruebas (`prueba-1`, `persona-cruza`, `vacia-1`) se borraron para empezar el lote 1 de
cero, con el móvil ya fijo en su sitio.
