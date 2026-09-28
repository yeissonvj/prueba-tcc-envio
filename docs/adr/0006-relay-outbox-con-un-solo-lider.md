# ADR-0006 · Relay del outbox con un solo líder

**Estado:** aceptada

## Contexto
El procesador escribe el cambio de estado y el mensaje a publicar en la misma transacción (`bandeja_salida`). Un relay lo publica después. Si varias instancias publicaran con `SKIP LOCKED`, dos cambios de la misma guía podrían salir **desordenados** (la instancia B publica la v6 antes de que A publique la v5).

## Decisión
`pg_try_advisory_xact_lock`: en cada ciclo solo la instancia que obtiene el candado publica, en orden de `id`, en lotes de hasta 500 enviados juntos (el productor idempotente conserva el orden por partición). Las demás no hacen nada en ese ciclo. Además, cada mensaje lleva la `version` de la guía para que los consumidores puedan descartar lo viejo.

## Consecuencias
- Verificado con Testcontainers: dos relays en paralelo publican cada cambio **una sola vez** y en orden de versión.
- El throughput del relay es el de una instancia (suficiente: lotes de 500 cada 250 ms). Si se volviera cuello de botella, la salida es CDC con Debezium.

## Alternativas descartadas
- **`SKIP LOCKED` con varias instancias:** más throughput, pero desordena por guía.
- **Debezium (CDC) desde el inicio:** más piezas que operar para el tamaño actual; queda como evolución.
