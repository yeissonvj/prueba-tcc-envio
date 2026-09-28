# ADR-0011 · Procesamiento paralelo por partición dentro de cada instancia

**Estado:** aceptada

## Contexto
La prueba de carga C1 mostró que el procesador drenaba ~90 eventos/s por instancia: cada instancia atendía sus particiones **un mensaje a la vez**, y cada mensaje es una transacción en PostgreSQL con su propio `fsync`. El tiempo se iba en esperar el disco, no en la CPU. Con el techo de 24 instancias (una por partición) no se alcanzarían los 5.000 ev/s del diseño.

## Decisión
`ConsumidorKafka` con `ParticionesEnParalelo = true` (implementado en `DespachadorParticiones`):
- El hilo de Kafka solo lee y **reparte** cada mensaje a la cola de su partición.
- **Un trabajador por partición** procesa en orden y marca el offset después de cada mensaje.
- **Contrapresión:** si la cola de una partición se llena, esa partición se pausa y se rebobina; se reanuda cuando la cola baja a la mitad.
- **Rebalanceo:** al revocar o perder una partición, su trabajador termina el mensaje en curso (máx. 30 s) y descarta la cola; el nuevo dueño relee desde el último offset y el inbox evita el doble efecto.
- Un trabajador que falla inesperadamente detiene el servicio (no se queda una partición parada en silencio).

El orden por guía se conserva porque todas las guías de una partición son atendidas por el mismo trabajador, en orden.

## Consecuencias
- Hasta 24 transacciones concurrentes por instancia; PostgreSQL agrupa los `fsync` (*group commit*).
- Resultados antes/después en [pruebas.md](../pruebas.md).
- Se activa en el procesador y en el consumidor principal del notificador; los tópicos de reintento siguen secuenciales (esperan un vencimiento).
- Verificado con Kafka real: 30 claves × 10 mensajes, orden por clave intacto y procesamiento concurrente; y con un `kill` del procesador en plena carga, reconciliación exacta.

## Alternativas descartadas
- **Micro-lotes por partición (varias filas por transacción):** más rendimiento aún, pero complica el inbox y el manejo de errores por mensaje. Queda como siguiente paso si hiciera falta.
- **`synchronous_commit=off`:** más rápido, pero una caída de PostgreSQL podría perder transacciones ya confirmadas al consumidor: rompe la promesa de cero pérdida.
