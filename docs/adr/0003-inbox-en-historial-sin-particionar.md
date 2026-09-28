# ADR-0003 · El historial es el inbox y no se particiona (por ahora)

**Estado:** aceptada

## Contexto
El borrador de la Fase 1 particionaba `shipment_events` por fecha de recepción con `PRIMARY KEY (event_id, received_at)`. Al implementarlo se detectó un **error de diseño**: en PostgreSQL la llave primaria de una tabla particionada debe incluir la columna de partición, así que `event_id` solo sería único **dentro de cada partición**. Un evento reintentado en otro mes no se detectaría como duplicado y el inbox dejaría de garantizar "nunca doble efecto".

## Decisión
`historial_eventos` sin particionar, con `id_evento` como llave primaria. Cumple dos roles: historial para auditoría y consulta, e inbox para la idempotencia.

## Consecuencias
- Idempotencia global garantizada por la base (verificado: 50 procesamientos concurrentes del mismo evento → 1 fila de historial, 1 mensaje en el outbox).
- Con ~19 M eventos/día la tabla crecerá rápido. **Salida prevista** cuando el volumen lo exija: separar un `inbox` pequeño (`id_evento` PK, purgado a los 30 días) del historial particionado por mes.

## Alternativas descartadas
- **Particionar y deduplicar con índice único por partición:** no garantiza unicidad global.
- **Deduplicar solo en Redis:** Redis es optimización, no garantía ([ADR-0004](0004-filtro-duplicados-tolerante.md)).
