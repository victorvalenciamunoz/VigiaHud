# Fase 3 · paso 1: primera grabación de prueba en el OPPO (2026-11-04)

Solo números: la grabación es del interior de la casa y está fuera del repo (`%USERPROFILE%\.vigiahud\secuencias\`).

## Qué se hizo

1. Instalada la versión con el grabador (`dotnet build src/VigiaHud.App -t:Install`). La vigilancia arrancó sola
   (`update: service start requested`, `service started`, `camera ready, 15-15 fps`, `telegram: bot started`).
2. El autor mandó `/grabar 1 prueba-1` desde su Telegram.
3. `scripts/pull-recordings.ps1` la trajo al PC y la borró del móvil tras comprobar tamaño y SHA-256.
4. Leída en el PC con `SequenceReader` de Core.

## Registro del móvil

```
2026-11-04T10:02:40.015Z	capture	frames=30 failed=0 period=60s
2026-11-04T10:03:19.012Z	info	record: started prueba-1 for 60 s
2026-11-04T10:03:19.163Z	info	telegram: answered /grabar
2026-11-04T10:03:40.117Z	capture	frames=30 failed=0 period=60s
2026-11-04T10:04:19.013Z	info	record: finished prueba-1: 60 frames, 1489025 bytes, completed
2026-11-04T10:04:40.289Z	capture	frames=30 failed=0 period=60s
```

La vigilancia no se enteró: 30 fotogramas por minuto sin fallos durante la grabación.

## La grabación, leída en el PC

| Dato | Valor |
|---|---|
| Fotogramas | 60, fichero entero (`Truncated` falso) |
| Cabecera | 160×120 desde 320×240, sensor a 90°, uno por segundo |
| Separación entre fotogramas | mínima 0,887 s, máxima 1,124 s, media 1,000 s |
| Imagen gris comprimida | 14,3 KB por fotograma (18,8 KB sin comprimir) |
| JPEG 320×240 | 9,9 KB por fotograma |
| Total | 1,42 MB por minuto, **89 MB por hora** (unos 1,1 GB en 12 h) |
| Copia | `sha256 36b34fc8…a398171`, igual en el móvil y en el PC |

## CPU (% de un núcleo, ventanas de 20 s con `/proc/<pid>/stat`)

| Momento | App | Cámara del sistema (proveedor) | `cameraserver` |
|---|---|---|---|
| Versión anterior | 21, 22, 23 | 105, 103, 103 | 10, 10, 10 |
| Versión nueva, sin grabar | 24, 17, 23 | 111, 81, 109 | 11, 8, 11 |
| Versión nueva, grabando | 26, 27 | 90, 88 | 10, 9 |

Sin grabar no cambia nada (la ventana de 17 % coincide con un bajón de la cámara del sistema). Grabando, la app sube
unos 4 puntos: copiar, comprimir y escribir un fotograma por segundo. Solo mientras dura la grabación.
