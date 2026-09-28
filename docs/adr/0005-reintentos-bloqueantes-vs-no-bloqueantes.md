# ADR-0005 · Reintentos bloqueantes en el procesador, no bloqueantes en el notificador

**Estado:** aceptada

## Contexto
Los dos consumidores fallan por razones distintas y lo que está en juego es distinto: en el procesador, saltar un evento rompe el orden de la guía; en el notificador, esperar a un proveedor caído frena a todas las demás guías.

## Decisión
**Procesador (orden sagrado):**
- Error transitorio (base caída, red, conflicto de versión) → reintento **bloqueante** con backoff exponencial + jitter (200 ms → 30 s), sin límite. Nada se salta.
- Mensaje ilegible (poison pill) → DLQ de inmediato.
- Error inesperado (bug) → 3 intentos y DLQ, para no congelar la partición.

**Notificador (flujo sagrado):**
- Proveedor no disponible → tópico de reintento (1 min → 10 min → 1 h); el consumidor de cada tópico pausa sus particiones hasta el vencimiento y sigue haciendo poll.
- Agotados los reintentos, destino rechazado o sin destino → canal alterno (correo) con su propia escalera → DLQ.
- Notificación de una versión más vieja que la última enviada → se descarta (el cliente nunca recibe "en reparto" después de "entregado").

## Consecuencias
- Verificado: con PostgreSQL caído, el procesador reintentó sin saltar y dejó la guía en v3 con los cambios en orden; con SMS caído, la escalera terminó en el correo y nada fue a la DLQ.
- La regla de "3 intentos ante error inesperado" se probó con un bug real (fechas no UTC): no congeló las 24 particiones y los eventos se recuperaron con replay.

## Alternativas descartadas
- **Reintentos no bloqueantes en el procesador:** reordenarían eventos de una guía.
- **Reintentos bloqueantes en el notificador:** un proveedor caído detendría todas las notificaciones.
