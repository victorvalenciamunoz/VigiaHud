# Estado

**Fase actual: 2 — Telegram.** Ver `PLAN.md`. Pasos 1 a 4 hechos en Core; queda el paso 5, en el móvil.

La **fase 1** (que sobreviva) se cerró el 2026-11-03: el juez de 72 h en cargador de pared pasó sin avisos. Su
historia completa está en `docs/historia.md`.

## Fase 2 — Telegram

### Qué funciona

- `VigiaHud.Core.Bot`: `IncomingMessage` (mensaje recibido: update, chat, texto, instante UTC), `BotCommand`
  (`/foto`, `/estado`, `/ayuda` y `/start`, desconocido), `CommandParser` (lee el comando tal como lo mandan los
  clientes de Telegram: con `@NombreDelBot`, con palabras detrás, en mayúsculas), `BotBrain` (decide: solo el chat del
  dueño recibe respuesta; cualquier otro se ignora sin contestar y deja una línea de registro con el chat y la longitud
  del texto, nunca el texto), `BotDecision` (ignorar, responder texto, mandar foto, mandar estado) y `BotTexts` (ayuda,
  "no te entiendo", estado y pie de foto en español, con la hora en la zona local que se le pasa; el estado avisa si
  falta la exclusión de batería).
- `VigiaHud.Core.Telegram` (DECISIONS 016 y 017): `ITelegramClient` con `TelegramClient` sobre `HttpClient` y
  `JsonDocument` (sin reflexión, apto para la app recortada): `GetUpdatesAsync` (long polling solo de mensajes, con
  offset; devuelve `UpdateBatch` con los mensajes y el offset siguiente, que avanza también con lo que no es mensaje),
  `SendMessageAsync` (JSON) y `SendPhotoAsync` (multipart con la foto JPEG y el pie). Cada petición lleva su propio
  plazo: el long poll más 15 s, o 60 s para enviar. Dos errores distintos para el paso 3: `TelegramApiException`
  (Telegram contestó con un error: código, descripción y `retry_after` si lo hay) y `TelegramUnavailableException`
  (sin red, sin respuesta a tiempo o una respuesta que no es de la Bot API). El token nunca aparece en un mensaje de
  error. Una cancelación desde fuera sigue siendo cancelación.
- `TelegramPoller` (DECISIONS 018): el bucle que mantiene al bot escuchando y no se rinde nunca. Entrega los
  mensajes en orden y solo los da por recibidos cuando están contestados: si contestar falla por falta de red, el
  mensaje vuelve a llegar. Sin Telegram espera 2 s, 4, 8… hasta 5 min, o lo que pida Telegram (`retry_after`); un
  token rechazado o un error inesperado esperan 5 min y se apuntan como `error`. Cada corte deja dos líneas en el
  registro (`telegram: offline (motivo)` y `telegram: online again after N s (M failed polls)`) y se entrega como
  `Outage` (inicio, fin, intentos, motivo) para avisar al dueño; si el aviso no sale por falta de red, se repite tras
  el siguiente corte. `BotTexts.Reconnected`: "Vuelvo a tener conexión. Estuve 12 min sin poder hablar con Telegram,
  desde hoy a las 11:00."
- Latido diario y cola de salida (DECISIONS 019):
  - `DailyHeartbeatSchedule` decide cuándo toca el "sigo vivo" de las 09:00 en la zona del móvil, con el cambio de
    hora; si uno no pudo salir a su hora sale en cuanto se puede, marcado como tardío. Una instalación nueva no debe
    nada hasta las 09:00 siguientes.
  - `Outbox` guarda en orden lo que el bot dice por su cuenta hasta que sale; si sale más de 2 min tarde dice de
    cuándo es ("(Con retraso: es de hoy a las 09:00.)"); máximo 50 mensajes.
  - `BotRunner` es el bot entero para la app: el bucle de Telegram contesta los comandos directamente (`/foto`,
    `/estado`, `/ayuda`) y el reloj, que se despierta al menos cada minuto y mira la hora real, escribe el latido y
    vacía la cola. Avisa al volver solo de los cortes de 5 min o más; todos cuentan en el resumen del latido.
    `IBotEnvironment` (zona, tiempo vigilando, estado del móvil, foto) y `IBotMemory` (último latido, que debe
    sobrevivir a reinicios) los dará la app en el paso 5.
  - Textos nuevos en `BotTexts`: `DailyHeartbeat` ("Sigo vivo." o "Sigo vivo (con retraso: a las 09:00 no pude).",
    tiempo vigilando, batería, cortes de las últimas 24 h y el aviso de exclusión de batería si falta) y
    `CameraNotReady`.
- Avisos de corriente y batería (DECISIONS 019 y 020): `PowerWatch` recibe cada minuto, del reloj de `BotRunner`, una
  lectura del móvil y dice qué contar, una vez cada cosa: `PowerNotice.Lost` a los 10 min sin cargador ("Sin
  corriente desde hoy a las 03:12: se ha desenchufado el cargador o se ha ido la luz. Batería al 84 %, unas 9 h de
  margen."), `Back` al volver si se avisó del corte ("Vuelve la corriente. Estuvo 1 h 20 min sin cargador; batería al
  72 %."), `LowBattery` al 15 % sin cargador ("me apagaré en unas 2 h") y `DozeExemptionLost` en cuanto falta la
  exclusión de batería, otra vez solo si vuelve y se pierde. Guarda todos los cortes de corriente (`PowerCut`), cortos
  incluidos, para el resumen del latido: "Últimas 24 h: sin cortes de conexión; 1 corte de corriente, de 4 min." El
  margen es `PowerWatch.HoursLeft`: el porcentaje entre 9 % por hora, redondeado a horas. Cada aviso deja una línea
  `power:` en el registro.
- En el PC: el bot existe (creado por el autor con @BotFather, sin poder unirse a grupos); el token y el chat ID del
  dueño están en los secretos de usuario con id `vigiahud`, fuera del repo. `scripts/record-telegram-fixtures.cs`
  graba respuestas reales de la Bot API con los datos personales sustituidos.
- En el móvil (paso 5, DECISIONS 021): `scripts/push-telegram-secrets.ps1` copia el token y el chat ID de los secretos
  del PC a `files/telegram.json`, en el almacenamiento privado de la app (permisos 600), sin fichero temporal ni nada
  en pantalla. `WatchService` lee ese fichero al arrancar (`TelegramSecrets` en Core) y pone en marcha `BotRunner` en
  su propio hilo; sin el fichero, la vigilancia sigue como en la fase 1 y lo apunta. `AndroidBotEnvironment` da la
  zona del móvil, la batería y el tiempo vigilando; `PreferencesBotMemory` guarda la hora del último latido.
  `/foto` toma una foto aparte de 1280×960 en la sesión de cámara que ya está en marcha (exposición ya ajustada, sin
  flash, con la orientación escrita en el JPEG: 90° para el móvil de pie); si la cámara no la da en 5 s, envía el
  último fotograma de la vigilancia. Cada respuesta deja `telegram: answered /estado|/foto|/ayuda|other text` en el
  registro, nunca las palabras del dueño.
- Menos CPU en la vigilancia: la conversión de cada fotograma a JPEG copiaba los colores píxel a píxel a través del
  puente con Java (unas 38 000 llamadas cada 2 s); ahora copia cada plano una vez. Medido con `/proc` en el OPPO, en
  ventanas de 20 s: la app pasa del 51 % al 21-23 % de un núcleo (el hilo de cámara, del 41 % al 10,6 %); el servicio de
  cámara del sistema pasa del 94 % (una medida) al 101-106 %, quizá por tener preparada la salida de la foto grande.

### Qué falta

- [x] Paso 1 — Comandos y dueño (2026-10-29).
- [x] Paso 2 — Cliente de Telegram propio con `HttpClient`, probado con respuestas reales grabadas (2026-10-29).
- [x] Paso 3 — Reconexión con espera creciente si cae el wifi o Telegram no responde, registrando cada corte
  (2026-10-29).
- [x] Paso 4 — Mensajes por su cuenta (DECISIONS 019), en dos partes:
  - [x] 4.1 Latido diario a las 09:00 con el resumen, con retraso si hace falta, y cola de salida (2026-10-29).
  - [x] 4.2 Avisos de corriente perdida (a los 10 min, con el margen estimado) y recuperada, batería baja al 15 % y
    exclusión de batería perdida; los cortes de luz cortos, solo en el resumen del latido (2026-10-29).
- [ ] Paso 5 — En el móvil (DECISIONS 021):
  - [x] Token y chat ID en el móvil fuera de git, el bot conectado al servicio, `/foto` a 1280×960 (2026-11-03).
  - [x] Latido diario con retraso, en el móvil (2026-11-03).
  - [ ] El primer latido diario a las 09:00 en punto (2026-11-04).
  - [ ] Juez de la fase 2: cortar el wifi del router 10 min y comprobar que vuelve solo y avisa del corte.
  - [x] Fotos derechas en cualquier posición del móvil, con el acelerómetro (DECISIONS 022, 2026-11-03).

### Cómo se validó

- Paso 1: 50 tests nuevos en `tests/VigiaHud.Core.Tests/Bot` (`CommandParserTests`, `BotBrainTests`,
  `BotTextsTests`): comandos con y sin nombre del bot, mayúsculas y texto detrás; todo lo de un extraño ignorado y
  registrado sin su texto; chat de grupo con ID negativo; textos exactos del estado con y sin datos, con aviso de
  exclusión de batería; duraciones; "hoy", "ayer" y fechas en hora local con una zona de prueba fija. `dotnet build`
  0 avisos (app incluida), `dotnet test` 102 tests.
- Paso 2: 24 tests nuevos en `tests/VigiaHud.Core.Tests/Telegram`. `TelegramClientTests` con un `HttpClient` falso
  (`Support/FakeHttpHandler`): la petición de `getUpdates` (dirección, offset, plazo, solo mensajes), el primer
  sondeo sin offset, el offset que avanza también con lo que no es mensaje, JSON de `sendMessage`, multipart de
  `sendPhoto`, error 429 con `retry_after` (forma de la documentación: no se puede provocar sin riesgo), 502 en HTML
  como no disponible, fallo de red sin el token en el mensaje, plazo agotado, cancelación desde fuera y tokens mal
  formados. `TelegramRecordedAnswersTests` con seis respuestas reales grabadas el 2026-10-29
  (`Fixtures/Telegram`): los cuatro mensajes del dueño (`/start`, `/foto`, un emoji, que llega como texto, y una
  pegatina, sin texto) con las decisiones de `BotBrain`, el sondeo vacío, el texto y la foto enviados, el 401 de un
  token falso y el 409 de dos sondeos a la vez. Los tests cazaron un fallo real: `new Uri(base, "bot123:ABC/...")`
  toma "bot123" por protocolo y descarta la dirección de Telegram; la dirección se monta ahora como texto.
  Prueba de extremo a extremo con el bot real (script desechable): el cliente de Core lee, envía el texto y la foto
  y recibe el 401 con un token falso. `dotnet build` 0 avisos (app incluida), `dotnet test` 126 tests.
- Paso 3: 14 tests nuevos en `TelegramPollerTests`, con el reloj simulado (`FakeTimeProvider`), un Telegram de guion
  (`Support/FakeTelegramClient`) y un registro en memoria (`Support/MemoryLog`): orden de los mensajes y offset;
  offset que avanza con lo que no es mensaje; esperas 2, 4, 8… 256, 300, 300 s comprobadas al milisegundo; las dos
  líneas de un corte y su `Outage`; vuelta a 2 s tras reconectar; `retry_after`; token rechazado como error con 5 min;
  error inesperado sin salir del bucle; mensaje que vuelve si contestar falla por la red y que se salta si falla por
  un fallo nuestro; aviso de corte repetido tras otro corte o descartado si el fallo no es de red; cancelación limpia;
  texto de reconexión. `dotnet build` 0 avisos (app incluida), `dotnet test` 140 tests (tres pasadas seguidas).
  Sin prueba de red real: la da el juez de la fase 2 en el móvil (paso 5).
- Paso 4.1: 29 tests nuevos. `DailyHeartbeatScheduleTests` con una zona genérica con horario de verano: antes y
  después de las 09:00, a su hora con medio minuto de margen, latido perdido hoy o ayer, instalación nueva, las 09:00
  locales a ambos lados del cambio de hora de octubre y una hora que no existe en marzo. `OutboxTests`: orden, sin
  red se queda para la próxima, un mensaje rechazado para siempre no atasca la cola, nota de retraso solo a partir de
  2 min, capacidad. `BotRunnerTests` con el bot entero y un Telegram de guion: `/estado`, `/foto` con y sin cámara,
  extraño ignorado, latido a las 09:00 en punto y recordado, latido perdido enviado al arrancar como tardío, latido
  sin red que sale a los 3 min con la nota de retraso, y el umbral de 5 min de los avisos de corte. `BotTextsTests`:
  los textos del latido. Un test del bucle de Telegram falló una vez de cada muchas: adelantaba el reloj simulado
  antes de que el bucle empezara a esperar. `Support/SteppingClock` avisa cuando alguien espera y los tests solo
  adelantan después; cinco pasadas seguidas sin fallos. `dotnet build` 0 avisos (app incluida), `dotnet test` 169
  tests.
- Paso 4.2: 25 tests nuevos. `PowerWatchTests`: siempre enchufado no dice nada; 10 min sin cargador avisan una vez con
  desde cuándo y la batería; un corte corto no avisa pero queda para el resumen; el final de un corte avisado dice su
  duración; batería baja una vez al 15 %, otra vez solo tras cargar y volver a perder la corriente, nada si está
  cargando; exclusión perdida una vez, otra solo tras recuperarla; los cortes viejos se olvidan; el margen a 9 % por
  hora. `BotTextsTests`: los cuatro textos nuevos y el resumen con cortes de conexión y de corriente.
  `BotRunnerTests`: el aviso de corriente sale a los 10 min simulados, ni uno antes, y el de exclusión al arrancar.
  `dotnet build` 0 avisos (app incluida), `dotnet test` 194 tests (cinco pasadas seguidas).
- Paso 5 (2026-11-03): 14 tests nuevos (`TelegramSecretsTests`: el fichero bueno, chat de grupo, diez formas de
  fichero malo, entre ellas un chat ID entre comillas que hacía saltar una excepción, y que imprimirlo no muestra el
  token; `BotRunnerTests`: cada respuesta deja su línea sin las palabras del dueño). En el OPPO: el script copió los
  secretos (78 bytes, `-rw-------`; la primera vez llevaban delante la marca BOM que añade Windows PowerShell, ya
  evitada); tras instalar, `telegram: bot started` sin errores; el autor mandó `/estado`, `/ayuda`, un texto y `/foto`
  desde su Telegram y recibió el estado, la lista, el "No te entiendo" y la foto; con la foto grande, `/foto` llegó a
  1280×960 y derecha con el móvil de pie (`telegram: answered /foto`, sin respaldo). La vigilancia siguió con
  `frames=30 failed=0`. `dotnet build` 0 avisos (app incluida), `dotnet test` 208 tests (tres pasadas seguidas).
- Latido con retraso en el móvil (2026-11-03): en las preferencias de la app se puso como último latido el
  2026-11-01 a las 09:00 locales y se relanzó el proceso (`am crash`; Android lo relanzó en 3 s con
  `service restarted by Android`). Al arrancar, el bot escribió `telegram: daily heartbeat written (late)` y el autor
  recibió "Sigo vivo (con retraso: a las 09:00 no pude).", el tiempo vigilando (0 min, desde el relanzamiento), la
  batería y "Últimas 24 h: sin cortes de conexión ni de corriente.". Ese envío cuenta como el latido del día: el
  siguiente toca el 2026-11-04 a las 09:00 en punto.
- Orientación automática (2026-11-03): `PhotoOrientation` en Core (8 + 4 + 5 casos de prueba: de pie, tumbado a cada
  lado, boca abajo, torcido, empate a 45°, plano y el giro del JPEG); `GravityReader` en la app toma una sola lectura
  del acelerómetro por foto. En el OPPO, con `/foto` de pie, tumbado con la parte de arriba a la derecha y a la
  izquierda: el registro detectó 0, 90 y 270 y escribió 90, 180 y 0 en el JPEG; el autor recibió las tres derechas.
  Tiempos con la foto grande: la cámara la da en 0,6-1,0 s (187-224 KB) y la subida a Telegram tarda 0,6-0,7 s; unos
  envíos anteriores tardaron 8-20 s en total y no se repitieron con un wifi de -53 dBm a 433 Mbps: el registro
  apunta ahora el tamaño y el tiempo de la cámara para saber qué parte es la lenta si vuelve a pasar. `dotnet test`
  225 tests.
