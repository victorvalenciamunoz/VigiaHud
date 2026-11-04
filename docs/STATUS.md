# Estado

**Fase actual: 3 — Movimiento.** Ver `PLAN.md`; el plan de la fase y lo decidido con el autor, en `DECISIONS.md` 024.

Las fases 1 (que sobreviva) y 2 (Telegram) están cerradas; su historia completa está en `docs/historia.md`.

## Fase 3 — Movimiento (desde el 2026-11-04)

### Qué funciona

- Margen de batería de los avisos al 5,3 % por hora medido en la fase 2 (`PowerWatch.BatteryPercentPerHour`): con el
  84 % el aviso de corriente perdida dice "unas 16 h de margen", no "unas 9 h".
- `VigiaHud.Core.Motion.LumaImage`: la imagen en gris (el plano Y de la cámara) y `Reduce`, que la encoge haciendo la
  media redondeada de cada bloque, salta el relleno de final de fila de los búferes de Android y descarta lo que no
  llena un bloque. 320×240 pasa a 160×120 para grabar.
- `VigiaHud.Core.Recording` (DECISIONS 024):
  - Formato de las grabaciones (`.vseq`): `SequenceWriter` escribe la cabecera (`SequenceHeader`: nombre, inicio,
    tamaños, montaje del sensor, cadencia) y cada fotograma (`SequenceFrame`: instante, imagen gris comprimida sin
    pérdida y JPEG en color) de una vez y a disco. `SequenceReader` los lee de uno en uno; un fichero cortado a mitad
    de un fotograma se lee hasta el último entero y lo dice (`Truncated`); cualquier otro daño dice qué fotograma.
  - `RecordingCommand` lee `/grabar 2 salida-1`, `/grabar 90s`, `/grabar 8h casa-vacia`, `/grabar parar` y
    `/grabar` solo (cómo se usa). De 10 s a 12 h; el nombre, una palabra en minúsculas con letras sin tilde, números y
    guiones, hasta 40 caracteres, porque va en el nombre del fichero.
  - `Recorder`: una grabación cada vez, en `aaaaMMdd-HHmmssZ-nombre.vseq`. Guarda un fotograma por segundo de los que
    la cámara ya da (15 por segundo), sin tocar la vigilancia. Se para sola al acabar su tiempo, con menos de 1,5 GB
    libres, si falla una escritura o al pararse la vigilancia; cada inicio y final deja una línea `record:` en el
    registro.
- Bot: `/grabar` (`BotCommand.Record`, `BotDecision.Record`). Contesta al empezar ("Grabo «salida-1» durante 2 min,
  una foto por segundo. Te aviso al terminar."), si ya hay otra en marcha, si falta espacio y si se corta. El reloj de
  `BotRunner` avisa cuando termina sola, con las fotos y el tamaño. `/ayuda` la incluye. Sin grabador, `/grabar` dice
  que ahora no puede grabar.
- En el móvil (paso 1.2): `WatchService` crea el `Recorder` en `files/recordings` (almacenamiento privado, con el
  espacio libre leído con `StatFs`) y se lo pasa al bot y a `CameraCapturer`. La cámara le pregunta en cada fotograma
  si toca grabar; si toca, una sola copia de los planos y un solo JPEG sirven a la vigilancia y a la grabación.
- `scripts/pull-recordings.ps1` trae las grabaciones a `%USERPROFILE%\.vigiahud\secuencias\` byte a byte
  (`adb exec-out`), comprueba tamaño y SHA-256 contra el móvil y solo entonces las borra de allí (`-Keep` las deja).
  Deja en el móvil la que aún se está escribiendo y se niega a copiar dentro del repo.

### Qué falta

- [x] Paso 1 — Grabar secuencias en el móvil y traerlas al PC (2026-11-04):
  - [x] 1.1 En Core: imagen gris pequeña, formato de las grabaciones, `/grabar` y el grabador; margen de batería al
    5,3 %/h.
  - [x] 1.2 En el móvil: el grabador en el servicio de vigilancia, el script que trae las grabaciones al PC y la
    prueba de 1 min con su tamaño y su CPU.
- [ ] Paso 2 — Consola con `info` y `hoja`; el autor graba y etiqueta el primer lote.
- [ ] Paso 3 — Detector y juez en el PC (`juzgar`, `barrer`); primera tabla y elección de umbrales.
- [ ] Paso 4 — Detector en el móvil: solo registro, foto guardada y recuento en el latido. CPU y batería antes y
  después (como mucho 5,8 %/h y la app por debajo del 25 % de un núcleo).
- [ ] Paso 5 — Editor de máscaras (el dibujo se confirma con el autor antes de programarlo).
- [ ] Paso 6 — Juez de la fase: lote grabado en la segunda residencia después de fijar los umbrales; tabla aquí.

### Cómo se validó

- Paso 1.1: 87 tests nuevos.
  - `LumaImageTests`: medias redondeadas, relleno de fila, bordes que no llenan un bloque y tamaños inválidos.
  - `SequenceFileTests`: ida y vuelta exacta, grabación sin fotogramas, el fichero cortado en cada uno de los bytes del
    último fotograma, cabecera cortada, fichero ajeno, fotograma dañado con su número, y una imagen lisa que comprimida
    ocupa menos de 1 KB.
  - `RecordingCommandTests`: duraciones con y sin unidad, nombres válidos y no válidos (espacios, tildes, `../`, más de
    40), límites de 10 s y 12 h.
  - `RecorderTests`, con el reloj simulado y la cámara a 15 fotogramas por segundo: un fotograma por segundo durante
    todo su tiempo, una sola a la vez, `parar`, fin sin fotogramas ni fichero, sin espacio al empezar y a mitad,
    espacio desconocido, hueco de la cámara sin recuperar lo perdido, escritura que falla, fichero cortado y líneas del
    registro.
  - `BotRunnerTests`: empezar, terminar sola avisada por el reloj, `parar` dos veces, y uso, error y falta de grabador.
    Además, textos (`BotTextsTests`) y el margen a 5,3 %/h (`PowerWatchTests`).
  - `dotnet build` de la solución con 0 avisos (app incluida); `dotnet test` 318 tests, cinco pasadas seguidas.
- Paso 1.2 en el OPPO (`verificacion/2026-11-04-fase3-paso1-grabacion.md`): el autor mandó `/grabar 1 prueba-1`;
  `record: finished prueba-1: 60 frames, 1489025 bytes, completed` y la vigilancia siguió con `frames=30 failed=0`.
  El script la trajo con la misma huella que en el móvil y la borró de allí. Leída con `SequenceReader`: 60
  fotogramas enteros, uno por segundo (de 0,89 a 1,12 s), 14,3 KB de imagen gris y 9,9 KB de JPEG por fotograma:
  **89 MB por hora**, unos 1,1 GB por noche de 12 h. CPU de la app: 21-23 % de un núcleo con la versión anterior,
  17-24 % con la nueva sin grabar y 26-27 % grabando; la cámara del sistema no cambia.

## Por ver de las fases cerradas

- La exclusión de batería se pierde al reiniciar y también al desenchufar (2026-11-03, 1 min después). La vigilancia
  aguanta sin ella y el bot avisa; si vuelve a pasar enchufado, buscar qué ajuste de ColorOS la quita.
- A batería y de noche, ColorOS dejó el móvil 5 h 15 min sin red (2026-11-04). En un corte de luz real el router
  también se apaga, pero si se desenchufa el móvil con el router encendido conviene saber qué ajuste lo provoca.
