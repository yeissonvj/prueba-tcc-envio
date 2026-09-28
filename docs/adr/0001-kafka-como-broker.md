# ADR-0001 · Apache Kafka como broker de eventos

**Estado:** aceptada

## Contexto
Varios sistemas (TMS, transporte, internacional, automatización) reportan eventos de guías; varios consumidores independientes los necesitan (estado, notificaciones, integraciones, agentes IA). Se requiere orden por guía, absorber picos ×4 y poder reprocesar si un despliegue introduce un error.

## Decisión
Kafka (KRaft, 3 brokers, `replication.factor=3`, `min.insync.replicas=2`), con el **número de guía como clave de partición** y 24 particiones.

## Consecuencias
- Orden por guía sin bloqueos distribuidos; cada consumidor avanza a su ritmo (grupos de consumo aislados).
- **Replay real:** en el paso 6 un bug envió 28 eventos válidos a la DLQ; tras corregirlo se reinició el grupo al inicio del tópico y se recuperaron sin reingresar datos ni duplicar.
- Techo de paralelismo = número de particiones (mitigado con [ADR-0011](0011-procesamiento-paralelo-por-particion.md)).
- Más complejidad operativa → en producción, Kafka administrado.

## Alternativas descartadas
- **RabbitMQ:** excelente para colas de tareas; orden por clave y replay son más difíciles. Sigue siendo opción válida para el notificador.
- **REST síncrono entre sistemas:** acopla disponibilidades y propaga fallas en cascada.
