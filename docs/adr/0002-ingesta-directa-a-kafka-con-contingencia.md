# ADR-0002 · La API publica directo a Kafka; contingencia en PostgreSQL con circuito

**Estado:** aceptada

## Contexto
El 202 debe significar "durable". Guardar primero en base de datos y luego publicar añadiría una escritura y un cuello de botella al camino crítico. Pero si Kafka no responde, la API no puede perder el evento ni colgar miles de conexiones esperando el timeout.

## Decisión
- Camino normal: publicar en Kafka con `acks=all` e idempotencia y responder 202 solo con la confirmación.
- `RecibirEvento → Contingencia( Circuito( Kafka ) )`:
  - **Circuito (Polly):** ante fallas sostenidas se abre 15 s y falla al instante en vez de esperar `delivery.timeout.ms` (5 s).
  - **Contingencia:** si Kafka no confirma, el evento se guarda en `contingencia_eventos` (PostgreSQL, idempotente por `id_evento`) y se responde 202.
  - Si tampoco hay PostgreSQL: **503 + Retry-After**.
- Un servicio de fondo reenvía la contingencia con `FOR UPDATE SKIP LOCKED` (varias instancias en paralelo sin tomar las mismas filas).

## Consecuencias
- Medido: con Kafka caído, los primeros eventos tardan ~6 s y, con el circuito abierto, **~0,1 s**; los 8 eventos de la prueba llegaron a Kafka al recuperarse.
- PostgreSQL solo se toca cuando Kafka falla: no afecta el rendimiento normal.
- Durante la recuperación el orden por guía no está garantizado ([ADR-0010](0010-orden-durante-recuperacion-de-contingencia.md)).

## Alternativas descartadas
- **Disco local:** un pod que muere en Kubernetes pierde su disco.
- **Redis como contingencia:** su durabilidad no es la de una base transaccional.
- **Outbox en la API para todo:** convierte a PostgreSQL en el cuello de botella de la ingesta.
