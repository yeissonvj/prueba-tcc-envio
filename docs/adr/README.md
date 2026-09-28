# Registros de decisiones de arquitectura (ADR)

Cada decisión importante queda escrita con su contexto, lo que se decidió, sus consecuencias y lo que se descartó. Cualquier persona del equipo puede proponer uno (ver [liderazgo](../liderazgo.md)): un ADR se discute en un PR como el código.

| # | Decisión | Estado |
|---|---|---|
| [0001](0001-kafka-como-broker.md) | Apache Kafka como broker de eventos | Aceptada |
| [0002](0002-ingesta-directa-a-kafka-con-contingencia.md) | La API publica directo a Kafka; contingencia en PostgreSQL con circuito | Aceptada |
| [0003](0003-inbox-en-historial-sin-particionar.md) | El historial es el inbox y no se particiona (por ahora) | Aceptada |
| [0004](0004-filtro-duplicados-tolerante.md) | Redis como filtro de duplicados tolerante a fallas | Aceptada |
| [0005](0005-reintentos-bloqueantes-vs-no-bloqueantes.md) | Reintentos bloqueantes en el procesador, no bloqueantes en el notificador | Aceptada |
| [0006](0006-relay-outbox-con-un-solo-lider.md) | Relay del outbox con un solo líder (advisory lock) | Aceptada |
| [0007](0007-migraciones-sql-compartidas-con-flyway.md) | Migraciones SQL versionadas con Flyway, compartidas por .NET y Java | Aceptada |
| [0008](0008-seguridad-oauth2-y-autorizacion-por-origen.md) | OAuth2 client credentials y autorización por origen | Aceptada |
| [0009](0009-observabilidad-opentelemetry.md) | OpenTelemetry con Collector; contexto de traza guardado en el outbox | Aceptada |
| [0010](0010-orden-durante-recuperacion-de-contingencia.md) | Orden no garantizado mientras se vacía la contingencia | Aceptada (riesgo conocido) |
| [0011](0011-procesamiento-paralelo-por-particion.md) | Procesamiento paralelo por partición dentro de cada instancia | Aceptada |
| [0012](0012-nombres-en-espanol-y-diseno-portable.md) | Nombres en español y diseño portable a Java/Spring | Aceptada |
