# ADR-0009 · OpenTelemetry con Collector; contexto de traza guardado en el outbox

**Estado:** aceptada

## Contexto
En un sistema asíncrono, entender "qué le pasó al evento X" exige seguirlo a través de la API, Kafka, el procesador, la base de datos y el notificador. Además, los SLO deben medirse, no suponerse.

## Decisión
- **OpenTelemetry** en los tres servicios, exportando OTLP a un **Collector**, que reparte a Jaeger (trazas) y Prometheus (métricas). Las apps no conocen los backends.
- El contexto W3C (`traceparent`) viaja en los **encabezados de Kafka** (instrumentado en `ProductorKafka` y `ConsumidorKafka`, los únicos puntos de entrada y salida) y se **guarda en `bandeja_salida`**, porque el relay publica después y desde otro hilo.
- Métricas de negocio y de SLO propias (`tcc_*`), lag por grupo (kafka-exporter) y alertas de Prometheus alineadas con los SLO.
- Logs por OTLP hacia **Loki** (vía el mismo Collector), consultables en Grafana: cada registro lleva `trace_id` y un enlace directo a su traza en Jaeger. Además, logs JSON con `TraceId`/`SpanId` en la consola fuera de Desarrollo (para `docker logs` y Kubernetes).

## Consecuencias
- Verificado: una traza de 11 spans recorre `POST → publish → procesador → SQL → outbox → notificador → SQL`.
- Hallazgos corregidos: el exportador de métricas envía cada 60 s por defecto (se ajustó a 10 s); `localhost` resolvía a IPv6; Jaeger 2.21 solo sirve su API v3 (se retiró la fuente de Jaeger en Grafana; las trazas se ven en la interfaz de Jaeger); kafka-exporter reporta lag −1 en particiones sin commit (`clamp_min`).

- Logs: la aplicación registra la consola **antes** que el proveedor de OpenTelemetry, porque `ClearProviders()` quitaría también ese proveedor y los logs dejarían de exportarse en silencio. Verificado provocando una caída de Redis: las 10 advertencias llegaron a Loki con `trace_id` y esas trazas existen en Jaeger.

## Alternativas descartadas
- **Promtail/Alloy leyendo los logs de Docker:** exige montar el socket de Docker y parsear texto; con OTLP la severidad y el `trace_id` llegan como datos estructurados y las apps no saben nada de Loki.
- **Exportar directo desde las apps a cada backend:** acopla el código a los productos de observabilidad.
- **Solo logs:** no permiten medir latencias de extremo a extremo ni reconstruir el recorrido.
