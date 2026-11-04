# Estado

**Fase actual: 3 — Movimiento.** Ver `PLAN.md`. Sin empezar: falta proponer cómo dividirla.

Las fases 1 (que sobreviva) y 2 (Telegram) están cerradas; su historia completa está en `docs/historia.md`.

## Por ver de las fases cerradas

- La exclusión de batería se pierde al reiniciar y también al desenchufar (2026-11-03, 1 min después). La vigilancia
  aguanta sin ella y el bot avisa; si vuelve a pasar enchufado, buscar qué ajuste de ColorOS la quita.
- A batería y de noche, ColorOS dejó el móvil 5 h 15 min sin red (2026-11-04). En un corte de luz real el router
  también se apaga, pero si se desenchufa el móvil con el router encendido conviene saber qué ajuste lo provoca.
- El margen de batería de los avisos usa el 9 % por hora de la fase 1 (DECISIONS 020); ahora se gastan 5,2-5,3 % por
  hora, así que el aviso se queda corto (dice unas 9 h donde hay unas 15). Pendiente de decidir si se actualiza.
