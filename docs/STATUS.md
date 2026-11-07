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
- Etiquetas (DECISIONS 025): `SequenceLabels` lee el `.txt` que va junto a cada grabación (`persona 00:12-00:31` por
  tramo con alguien, `#` para comentarios; sin fichero, sin etiquetar; sin líneas `persona`, sin nadie) y dice en qué
  línea está el error. `PersonInterval.Contains` cuenta enteros el primer y el último segundo del tramo.
  `SequenceSummary` resume una grabación: fotos, duración, hueco máximo, si está cortada, tamaño y SHA-256.
- `VigiaHud.Console` (`dotnet run --project src/VigiaHud.Console -- <comando>`), con las grabaciones por defecto en
  `%USERPROFILE%\.vigiahud\secuencias\`:
  - `info`: la tabla Markdown de `docs/secuencias.md`, con las grabaciones dañadas o las etiquetas mal escritas
    señaladas (y código de salida 1).
  - `hoja <grabación>`: una página junto a la grabación, con las fotos dentro, para mirarlas y etiquetarlas tocando
    cada foto con alguien (con Mayúsculas, todas desde el toque anterior); las marcadas seguidas son un tramo y COPIAR
    da las líneas del `.txt`. Lo ya etiquetado que la hoja no enseña se conserva. Como mucho 600 fotos repartidas;
    `--desde`, `--hasta` y `--cada N` para un rato concreto. Cuenta como persona cualquier parte del cuerpo. ACLARAR
    sube el brillo de las miniaturas (×3, ×6) para ver las grabaciones oscuras.
- `docs/secuencias.md`: cómo se graba y se etiqueta, las escenas del lote 1 y la lista de grabaciones.
- Detector (DECISIONS 026): `MotionDetector` en `VigiaHud.Core.Motion` encoge cada foto gris a 80×60, la compara con la
  anterior o con un fondo que aprende despacio, descuenta la mediana del cambio de brillo, cuenta los píxeles que
  pasan el umbral fuera de las zonas ignoradas (`MotionMask`) y decide `Still`, `Changed`, `Motion` (área mínima
  durante N fotos seguidas) o `LightChange` (casi toda la imagen). Sus valores (`MotionSettings`) son de partida.
- Juez (DECISIONS 027): `MotionJudge` en `VigiaHud.Core.Judge` pasa una grabación por el detector como el móvil y la
  cruza con sus etiquetas: avisos (con cooldown), tramos detectados (con margen de adelanto y de cola) y falsas
  alarmas. `juzgar` en la consola saca la tabla de todas las grabaciones etiquetadas, con los valores que se le den
  (`--umbral`, `--area`, `--seguidos`, `--referencia fondo`, `--cada 2`, `--cooldown`, `--adelanto`…).

### Qué falta

- [x] Paso 1 — Grabar secuencias en el móvil y traerlas al PC (2026-11-04):
  - [x] 1.1 En Core: imagen gris pequeña, formato de las grabaciones, `/grabar` y el grabador; margen de batería al
    5,3 %/h.
  - [x] 1.2 En el móvil: el grabador en el servicio de vigilancia, el script que trae las grabaciones al PC y la
    prueba de 1 min con su tamaño y su CPU.
- [ ] Paso 2 — Mirar y etiquetar las grabaciones:
  - [x] La consola con `info` y `hoja`, y el formato de las etiquetas (2026-11-04).
  - [ ] El autor graba y etiqueta el lote 1 (`docs/secuencias.md`). Hechas y etiquetadas: `vacia-1`, `persona-cruza`,
    `persona-quieta`, `persona-lenta`, `persona-hacia`, `cortina`, `luz-techo` y `persona-poca-luz`. Faltan
    `persona-lejos`, `persona-noche`, `sol` y `casa-vacia` (con el móvil enchufado); `tele-reflejo`, opcional. Fuera
    `tele` y `luz-lampara`: la cámara no verá una tele y no hay lámpara.
- [ ] Paso 3 — Detector y juez en el PC:
  - [x] 3.1 El detector en Core (2026-11-07).
  - [x] 3.2 El juez (avisos, cooldown, aciertos y fallos) y `juzgar`; primera tabla (2026-11-07).
  - [ ] 3.3 `barrer` y la elección de los valores con el autor; antes, `casa-vacia`.
  - Pregunta abierta: ¿hacen falta máscaras? Se decide con los números del juez: si las falsas alarmas salen de una
    zona fija, sí; si son sombras repartidas o luz, se propondrá quitarlas del plan y dejarlo a la fase 4.
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
- Paso 2: 32 tests nuevos (`SequenceLabelsTests`: orden y comentarios, sin nadie, minutos y horas, retorno de carro
  de Windows, cada error con su línea, los extremos de un tramo, ida y vuelta de las horas; `SequenceSummaryTests`:
  fotos, duración, hueco, fichero cortado y huella). Con la grabación real `prueba-1`: `info` saca su fila y `hoja` su
  página, entera (60 fotos) y de un rato (`--desde 00:20 --hasta 00:29`, 10 fotos); los errores de uso dan un mensaje
  claro. El JavaScript de la página pasa `node --check`. El autor abrió la página en su navegador: fotos derechas,
  marcar, quitar y COPIAR bien. Al etiquetar `persona-cruza` tocó foto a foto y dejó fuera dos fotos en las que se le
  veía; la hoja pasó a marcar una foto por toque. Con un DOM simulado y sus etiquetas: tocar 00:21 y 00:22 junta
  `00:19-00:20` y `00:23-00:24` en `00:19-00:24`; desmarcar una foto parte el tramo; Mayúsculas marca todo el rato; y
  una hoja de un rato o de una foto cada 10 s conserva lo que no enseña. Las
  páginas no se miraron desde aquí: llevan las fotos de la casa. `dotnet build` de la solución con 0 avisos;
  `dotnet test` 350 tests, cinco pasadas seguidas. ACLARAR (2026-11-07): con un DOM simulado pasa por ×3, ×6 y normal.
- Paso 3.1: 23 tests nuevos en `MotionDetectorTests`, con fotos inventadas (una pared con grano y un cuadrado oscuro
  del 2 % de la imagen): el grano no es movimiento; el cuadrado que se mueve lo es a la segunda foto, o a la primera
  si se pide; un cambio que se para reinicia la cuenta; menos del área mínima no cuenta; la habitación 40 niveles más
  clara no es movimiento y el cuadrado se sigue viendo mientras se aclara; una foto que cambia entera es luz y
  reinicia la cuenta; lo que pasa en una zona ignorada no se ve, y la parte vigilada cuenta sobre su propia área;
  contra la foto anterior una persona quieta desaparece y contra el fondo se sigue viendo hasta que el fondo la
  absorbe; tras la luz el fondo se aprende de nuevo; `Reset`; sin encoger; valores fuera de rango; la rejilla de la
  máscara sobre cualquier tamaño. Con los valores de partida sobre las ocho grabaciones (solo números, un carácter por
  segundo): `vacia-1` nada; movimiento en todas las pasadas de día, empezando 2-3 s antes por la sombra; movimiento
  falso en `cortina` y en los encendidos de `luz-techo`; casi nada en `persona-poca-luz`. `dotnet test` 373 tests.
- Paso 3.2: 13 tests nuevos en `MotionJudgeTests` (películas inventadas de una foto por segundo con un cuadrado que
  cruza): casa quieta sin nada; persona detectada al segundo de entrar; persona que no se mueve, perdida; movimiento
  sin nadie, falsa alarma; movimiento 2 s antes cuenta para el tramo y 8 s antes es falsa alarma; segunda visita
  dentro del cooldown detectada sin aviso y fuera de él con aviso nuevo; movimiento falso dentro del cooldown no
  cuenta; dos visitas seguidas no comparten movimiento (lo cazó la primera tabla real); una foto cada 2 s; hueco de la
  cámara; zona ignorada. `dotnet test` 386 tests.

  Primera tabla (`juzgar` con los valores de partida, cooldown 2 min y adelanto 5 s; 8 grabaciones, 8 min):

  | Grabación | Con persona | Detectadas | Perdidas | Falsas alarmas | Primer movimiento (s desde que se le ve) |
  |---|---|---|---|---|---|
  | vacia-1 | 0 | 0 | 0 | 0 | |
  | persona-cruza | 3 | 3 | 0 | 0 | −1, 0, −3 |
  | persona-quieta | 1 | 1 | 0 | 0 | −1 |
  | persona-lenta | 1 | 1 | 0 | 1 | −4 |
  | persona-hacia | 1 | 1 | 0 | 0 | −2 |
  | cortina | 0 | 0 | 0 | 1 | |
  | luz-techo | 0 | 0 | 0 | 1 | |
  | persona-poca-luz | 7 | 0 | 7 | 0 | todas perdidas |
  | **Total** | 13 | 6 | 7 | 3 (22,5 por hora) | |

## Por ver de las fases cerradas

- La exclusión de batería se pierde al reiniciar y también al desenchufar (2026-11-03, 1 min después). La vigilancia
  aguanta sin ella y el bot avisa; si vuelve a pasar enchufado, buscar qué ajuste de ColorOS la quita.
- A batería y de noche, ColorOS dejó el móvil 5 h 15 min sin red (2026-11-04). En un corte de luz real el router
  también se apaga, pero si se desenchufa el móvil con el router encendido conviene saber qué ajuste lo provoca.
