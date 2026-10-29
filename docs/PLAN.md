# Plan

**Fase actual: 1** (esperando el veredicto del juez de 72 h; mientras tanto la fase 2 avanza solo en Core y tests,
sin tocar el móvil: `DECISIONS.md` 015)

---

## Fase 1 — Que sobreviva

Objetivo: un servicio en primer plano que hace una foto cada N segundos con la pantalla apagada
y sigue vivo durante al menos 24 h seguidas.

- Antes de nada: preguntar al usuario modelo y versión de Android del móvil que va a usar,
  y fijar con eso la versión mínima soportada (DECISIONS.md).
- App MAUI Android mínima: pantalla con botón Iniciar/Parar vigilancia y estado.
- Foreground service de tipo cámara, con notificación permanente. Se inicia con la app visible.
- Captura periódica (p. ej. cada 2 s) a baja resolución; guardar solo cada X minutos para depurar.
- Registro persistente: arranques, paradas, errores, número de capturas, batería y temperatura cada 10 min.
- Guiar al usuario para excluir la app de la optimización de batería.
- Plan B documentado si el servicio con pantalla apagada falla en ese móvil: pantalla encendida en negro
  con brillo mínimo (wake lock / keep screen on).
- Arranque automático tras reinicio del móvil: investigar qué permite Android para este caso y documentarlo.

**Juez:** dejarlo 24 h con la pantalla apagada y enchufado. El registro debe mostrar capturas continuas
sin huecos mayores de 1 minuto. Repetir 72 h antes de dar la fase por cerrada. Resultados en STATUS.md.

## Fase 2 — Telegram

- Bot con long polling (`getUpdates`); sin webhooks.
- Solo responde al chat ID autorizado; el resto se ignora y se registra.
- Comandos: `/foto`, `/estado`, `/ayuda`.
- Latido diario a una hora fija ("sigo vivo" + resumen: uptime, batería, temperatura, avisos del día).
- Aviso de cargador desconectado / reconectado.
- Reconexión automática si cae el wifi o Telegram no responde (con backoff), registrando cada corte.

**Juez:** cortar el wifi del router 10 minutos y comprobar que vuelve solo y avisa del corte.

## Fase 3 — Movimiento

- En Core: diferencia entre frames (escala de grises, reducción, umbral, área mínima).
- Máscaras de zonas a ignorar (ventanas, cortinas), definidas tocando sobre una foto en la app.
- Cooldown para no mandar 50 avisos seguidos.
- Consola: pasar la detección sobre secuencias grabadas y sacar métricas.

**Juez:** un conjunto de secuencias etiquetadas (con y sin persona, cambios de luz, sombras).
Tabla de falsas alarmas y detecciones perdidas en STATUS.md.

## Fase 4 — Personas

- Modelo de detección de personas en el dispositivo como confirmación tras el movimiento.
  Valorar opciones (licencia, tamaño, velocidad en un móvil viejo) y documentarlo.
- Solo se avisa si el modelo confirma persona.

**Juez:** mismas secuencias de la fase 3. Comparar la tabla antes y después del modelo.

## Fase 5 — Armado, ráfagas y detalles

- `/armar`, `/desarmar` (estado persistente: sobrevive a reinicios).
- Al detectar: ráfaga de 3-5 fotos o clip corto.
- Rotación de imágenes guardadas en el móvil (límite de espacio).
- Comportamiento nocturno: decidir qué hacer con poca luz (umbral distinto, avisar de baja visibilidad…).
