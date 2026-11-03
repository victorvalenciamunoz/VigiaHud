# Juez de la fase 2: corte de red de 10 minutos (2026-11-03)

Prueba sustituta del corte del wifi del router (`PLAN.md`): el autor no podía apagar el router, así que se apagó el
wifi del propio OPPO por `adb` (`svc wifi disable` / `enable`); el móvil no tiene datos móviles, así que se queda sin
internet igual que si cayera el router. Registro: `2026-11-03-fase2-juez-wifi.log`. Horas en UTC (local = UTC+1).

## Primer corte: vuelve solo y avisa, pero tarde (y un fallo)

| UTC | Qué pasó |
|---|---|
| 10:41:42 | Wifi apagado |
| 10:41:44 | `error telegram: poll failed: WebException: ...` y `info telegram: offline (...)` |
| 10:51:47 | Wifi encendido (internet comprobado) |
| 10:55:15 | `telegram: online again after 811 s (9 failed polls)` |
| 10:55:16 | `telegram: answered /estado` (mandado por el autor durante el corte) |

- El autor recibió la respuesta a `/estado` y "Vuelvo a tener conexión. Estuve 13 min sin poder hablar con
  Telegram, desde hoy a las 11:41.". Vuelve solo y avisa: lo que pide el juez.
- Fallo: en Android, el corte llega como `WebException`, que el cliente no reconocía como "sin red"; se trató como
  error inesperado (línea `error`, espera de 5 min de golpe). Y la espera creciente hizo que tardara 3,5 min en
  notar la vuelta del wifi. Arreglos en DECISIONS 023.

## Segundo corte, con los arreglos: PASA

| UTC | Qué pasó |
|---|---|
| 11:09:24 | Wifi apagado |
| 11:09:27 | `info telegram: offline (getUpdates: WebException: ...)`, sin línea `error` |
| 11:19:26 | Wifi encendido |
| 11:19:32 | `telegram: network back; retrying now` (aviso de Android) |
| 11:19:33 | `telegram: online again after 606 s (9 failed polls)`: 7 s después de volver el wifi |
| 11:19:34 | `telegram: answered /estado` (mandado por el autor durante el corte) |

- La vigilancia siguió capturando durante los dos cortes: todas las líneas `capture` de la ventana son
  `frames=30 failed=0`.
- Veredicto: **PASA**. Vuelve sola, avisa del corte con su duración, contesta lo que se le pidió durante el corte y
  no cuenta un corte de red como error.
