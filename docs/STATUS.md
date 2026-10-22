# Estado

**Fase actual: 1 — Que sobreviva** (en marcha). Ver `PLAN.md`.

## Hecho

### Paso 0 — El móvil y las reglas de Android (2026-10-22)

- Móvil leído por `adb`: OPPO A5 2020 (CPH1931), Android 10 (API 29), ColorOS 7.1, 3 GB de RAM, 4,8 GB libres.
  Mínimo soportado API 29; objetivo API 36 (DECISIONS 003).
- Verificado en la documentación oficial de Android: el tipo de servicio `camera` no existe en API 29 (llega en API 30),
  así que en este móvil el servicio se promociona sin tipo; la restricción "while-in-use" (Android 11), la prohibición de
  arrancar servicios desde segundo plano (Android 12) y la de arrancar servicios `camera` desde `BOOT_COMPLETED`
  (Android 15) no aplican en Android 10. `BOOT_COMPLETED` sigue siendo un receptor de manifiesto permitido.
  El riesgo real es ColorOS matando el proceso: ajustes del usuario (optimización de batería, inicio automático,
  ejecutar en segundo plano, candado en recientes), sin solución desde el código.
- Entorno: .NET 10.0.401 fijado con `global.json` (DECISIONS 004); los packs de Android 16 y las plantillas MAUI ya
  estaban instalados. `adb` está en `C:\Program Files (x86)\Android\android-sdk\platform-tools\`. El móvil solo aparece
  en `adb` con el USB en "Transferencia de archivos"; en modo MIDI es invisible.

### Paso 1 — Esqueleto (2026-10-22)

- Mismo esquema que CieloHud y VientoHud (DECISIONS 005): `VigiaHud.slnx`, `global.json`, `Directory.Build.props`
  (net10.0, nullable, avisos como errores), `.gitignore`, `.gitattributes`, `LICENSE` MIT, `THIRD-PARTY-NOTICES.md`,
  `licenses/` (OFL de Chakra Petch y Open Sans), `README.md` y CI de GitHub Actions (`Tests de Core`, solo Core y tests).
- `src/VigiaHud.Core` (vacío por ahora), `src/VigiaHud.App` (net10.0-android, mínimo API 29, `com.vigiahud.app`,
  versión 0.1.0 / 100) y `tests/VigiaHud.Core.Tests` (xUnit, un test humo que carga el ensamblado de Core).
- La app: tema oscuro de la familia (`Hud/HudTheme.cs`), fuentes Chakra Petch y Open Sans, icono y splash (un ojo en cian
  sobre `#0B1218`), y `WatchPage` con la cabecera, el estado de la vigilancia y el botón INICIAR (deshabilitado: se
  conecta al servicio en el paso 3). Solo Android: sin carpetas de iOS, Mac ni Windows.
- Validado: `dotnet build` de toda la solución (0 avisos, 0 errores, incluida la app Android) y `dotnet test` (1 test).
  `dotnet build src/VigiaHud.App -t:Install` instala en el OPPO (`minSdk=29 targetSdk=36 versionName=0.1.0` según
  `dumpsys package`); la app arranca sin errores en el registro (`logcat`): proceso vivo y ventana de `MainActivity`
  creada. El arranque en Debug tarda unos 15 s en este móvil. Pendiente la captura de pantalla: el bloqueo de ColorOS
  no se quita desde `adb` y hay que desbloquear a mano.

## Siguiente

- Paso 2: registro persistente en Core (interfaz, rotación por tamaño, línea con marca UTC) con tests, e implementación
  en ficheros en la app.
- Crear el repo `victorvalenciamunoz/VigiaHud` en GitHub, proteger `main` y abrir el pull request del paso 1.
