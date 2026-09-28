# Operación · manual de guardia

> Qué mirar, qué significa y qué hacer. Pensado para que cualquier persona del equipo (no solo quien lo construyó) pueda atender un incidente.

## Dónde mirar

| Herramienta | URL local | Para qué |
|---|---|---|
| Grafana · tablero "TCC · Plataforma de eventos de guías" | http://localhost:3000 | ¿Perdemos algo? ¿Cumplimos los SLO? ¿Dónde está el atasco? |
| Prometheus · alertas | http://localhost:9090/alerts | Qué regla se disparó |
| Grafana · fila "Logs" del tablero, o **Explore → Loki** | http://localhost:3000 | Logs de los tres servicios: advertencias y errores, búsqueda por texto, número de guía o `idEvento` |
| Jaeger | http://localhost:16686 | El recorrido de un evento concreto (buscar por servicio `tcc-api` y operación) |
| Kafka UI | http://localhost:8080 | Mensajes, particiones, DLQ, lag por grupo |
| Logs en la terminal | `docker compose logs <servicio>` | JSON con `TraceId`, útil si Loki no está disponible |

## Buscar en los logs (Grafana → Explore → Loki)

| Qué busco | Consulta LogQL |
|---|---|
| Todo lo de la plataforma | `{service_name=~"tcc-.+"}` |
| Solo advertencias y errores | `{service_name=~"tcc-.+"} \| severity_text=~"Warning\|Error\|Critical"` |
| Un servicio | `{service_name="tcc-procesador"}` |
| Una guía o un evento | `{service_name=~"tcc-.+"} \|= "TCC123456789"` |
| Mensajes enviados a DLQ | `{service_name=~"tcc-.+"} \|= "DLQ"` |

Al desplegar una línea aparece el botón **"Ver traza en Jaeger"**: abre el recorrido completo del evento que la produjo (API → Kafka → procesador → PostgreSQL → notificador). Retención de logs: 7 días. Los logs nunca contienen teléfonos ni correos completos (Ley 1581).

## Alertas y qué hacer

### `IngestaNoDurable` (página)
La API respondió 503: ni Kafka ni la contingencia guardaron eventos. Los emisores reintentarán con el mismo `idEvento` (no se pierde nada mientras el emisor conserve el evento).
1. `GET /salud/lista` → `Unhealthy` confirma que Kafka **y** PostgreSQL están fuera.
2. Recuperar primero **cualquiera** de los dos: con uno solo la API vuelve a responder 202.

### `IngestaEnContingencia` (ticket)
Kafka no confirma; los eventos se guardan en `contingencia_eventos` y se reenvían solos al recuperarse.
1. Revisar brokers en Kafka UI y el log del circuito (`Circuito hacia Kafka ABIERTO`).
2. Al volver Kafka, verificar que la tabla se vacía: `SELECT count(*) FROM contingencia_eventos;`.
3. Durante la recuperación puede haber eventos de una guía fuera de orden ([ADR-0010](adr/0010-orden-durante-recuperacion-de-contingencia.md)): revisar la métrica de `transicion_invalida`.

### `LagProcesadorCreciente` / `EstadoVisibleLentoP95` (página)
El procesador no alcanza al ritmo de ingesta.
1. ¿PostgreSQL responde? Si no, el procesador está reintentando (log `reintento bloqueante`): es lo esperado; se pone al día solo al volver la base.
2. Si PostgreSQL está bien: **escalar el procesador** (hasta 24 instancias, una por partición). En Compose: `docker compose --profile aplicaciones up -d --scale procesador=3`. En Kubernetes lo hace KEDA por lag.
3. Nunca "saltar" mensajes moviendo offsets hacia adelante: se perderían estados.

### `MensajesEnDlq` (ticket)
Hay mensajes que el sistema no puede procesar solo.
1. Leer el motivo en los encabezados (`dlq-motivo`, `dlq-topico-origen`, `dlq-particion-origen`, `dlq-offset-origen`) en Kafka UI.
2. Clasificar: ¿dato malo del emisor (hablar con el sistema dueño) o bug nuestro (corregir y desplegar)?
3. **Redrive** una vez corregida la causa: republicar el valor original en el tópico de origen. Es seguro: el inbox descarta lo que ya se hubiera aplicado.

### `NotificacionLentaP95` (ticket)
Un proveedor está fallando o lento. Los mensajes están en `notificaciones.reintento.*` o salieron por correo. El resto del sistema no se ve afectado. Contactar al proveedor; no hace falta intervenir la plataforma.

## Procedimientos

### Reprocesar desde Kafka (replay)
Útil si un bug marcó eventos como inválidos o los envió a la DLQ.
```bash
docker compose stop procesador
docker compose exec kafka-1 /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:19092 \
  --group procesador-estado --reset-offsets --to-datetime 2026-11-30T08:00:00.000 --topic guias.eventos.recibidos --execute
docker compose start procesador
```
Es seguro gracias al inbox: los eventos ya aplicados se reconocen como duplicados. Retención del tópico: 7 días.

### Reconciliación
Compara lo aceptado con lo guardado; la diferencia debe ser **cero**.
```bash
./pruebas-carga/reconciliar.sh <corrida>
```
En producción, un job diario compara los `idEvento` con 202 (logs de la API) contra `historial_eventos`.

### Congelamiento en temporada pico
Del 15 de noviembre al 10 de enero el pipeline bloquea despliegues a producción, salvo commits marcados `[hotfix]` con aprobación del comité.

## Detalles que conviene saber

- **Reinicios forzados y rebalanceos:** si un consumidor muere sin cerrar (`kill`, OOM), Kafka espera `session.timeout.ms` (45 s) antes de reasignar sus particiones. En Kubernetes con StatefulSet se recomienda *static membership* (`group.instance.id`) para que un reinicio rápido no provoque rebalanceo.
- **`/salud/viva` no revisa dependencias a propósito:** si revisara Kafka, una caída de Kafka reiniciaría todos los pods de la API en cadena.
- **`/salud/lista` = "puede guardar de forma durable en algún lado":** Kafka caído con contingencia disponible sigue recibiendo tráfico (`Degraded`).
- **Primer arranque:** la API precarga el productor de Kafka, la conexión a Redis y las llaves del emisor de tokens (primera petición ≈ 350 ms en vez de ≈ 2 s).
