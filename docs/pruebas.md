# Pruebas · estrategia y resultados

## Pirámide

| Nivel | Proyecto | Pruebas | Qué garantiza |
|---|---|---|---|
| Dominio | `TccEventos.Dominio.Pruebas` | 28 | Máquina de estados, reglas de aplicación de eventos y de notificación |
| Aplicación | `TccEventos.Aplicacion.Pruebas` | 23 | Casos de uso y decoradores (tolerancia, contingencia) con falsos escritos a mano |
| API (en memoria) | `TccEventos.Api.PruebasIntegracion` | 51 | Contratos HTTP 202/200/400/401/403/404/413/429/503, seguridad, sondas, **contrato OpenAPI**, circuito |
| Procesador | `TccEventos.Procesador.PruebasIntegracion` | 29 | Política de reintentos/DLQ y **10 pruebas contra PostgreSQL 17 y Kafka reales (Testcontainers)** |
| Notificador | `TccEventos.Notificador.Pruebas` | 12 | Escalera de reintentos, canal alterno, DLQ |
| Arquitectura | `TccEventos.Arquitectura.Pruebas` | 8 | Dependencias de la hexagonal y dominio sin setters públicos |
| **Total** | | **151** | ~70 s, sin depender del entorno local (los contenedores de prueba son desechables) |

```bash
dotnet test dotnet/TccEventos.slnx
```

### Pruebas imprescindibles de la Fase 2.2 (todas automatizadas)

| Prueba | Dónde |
|---|---|
| Duplicado responde 200 sin efecto | `EventosGuiaEndpointPruebas` |
| **50 procesamientos concurrentes del mismo evento → un solo efecto** | `RepositorioGuiasPostgresPruebas` (PostgreSQL real) |
| Evento tardío | `RepositorioGuiasPostgresPruebas`, `ProcesarEventoPruebas` |
| Falla entre escrituras (conflicto de versión → rollback completo) | `RepositorioGuiasPostgresPruebas` |
| Kafka caído → contingencia; ambos caídos → 503 | `PublicadorConContingenciaPruebas`, `EventosGuiaEndpointPruebas` |
| Poison pill → DLQ sin frenar la partición | `ManejadorMensajeRecibidoPruebas` |
| Tabla de transiciones | `MaquinaEstadosPruebas` |
| Orden por guía con particiones en paralelo | `ConsumidorKafkaPruebas` (Kafka real) |
| Relay sin duplicados ni desorden con dos instancias | `RelayBandejaSalidaPruebas` (PostgreSQL + Kafka reales) |

## Pruebas de caos (manuales, contra el sistema completo en Docker)

| Escenario | Resultado |
|---|---|
| Kafka caído | 202 desde contingencia (~6 s los primeros; ~0,1 s con el circuito abierto); al volver, los 8 eventos llegaron a Kafka |
| Kafka + PostgreSQL caídos | 503 inmediato |
| 1 / 2 brokers caídos | `/salud/lista` → `Healthy` / `Degraded` (ISR < 2 = contingencia) |
| PostgreSQL caído con eventos llegando | El procesador reintenta sin saltar; al volver, la guía en v3 con los cambios en orden |
| Mensaje ilegible escrito directo en Kafka | DLQ con motivo, partición y offset de origen |
| Bug que fallaba todos los eventos (fechas no UTC) | 28 eventos a la DLQ sin congelar particiones; **recuperados con replay** del grupo de consumo |
| Proveedor de SMS caído | Reintentos 5/10/15 s (escala de desarrollo) → correo; 0 en DLQ |
| Replay completo del notificador | 0 SMS repetidos (idempotencia por `guía:versión:canal`) |
| **`kill` del procesador en plena carga (C2)** | Reconciliación exacta: 53.827 = 53.827 |

## Carga (k6) y reconciliación

**Entorno:** un portátil (8 CPU lógicas, 24 GB) que corre al mismo tiempo 3 brokers de Kafka, PostgreSQL, Redis, Keycloak, el stack de observabilidad, k6 y los tres servicios (imágenes de producción, una instancia de cada uno). **No es representativo de producción**: sirve para encontrar cuellos de botella y demostrar la corrección bajo carga, no para certificar capacidad.

```bash
docker run --rm --network tcc-eventos_default -v "$PWD/pruebas-carga:/pruebas" \
  -e CORRIDA=C1 -e ETAPAS=200:45s,500:45s,1000:45s grafana/k6:2.3.0 run /pruebas/k6/ingesta.js
./pruebas-carga/reconciliar.sh C1      # Git Bash o Linux
```

### Resultado principal: cero pérdida

| Corrida | Condición | Aceptados (202) | En PostgreSQL | Diferencia |
|---|---|---|---|---|
| C1 | Rampa 200 → 500 → 1.000 ev/s | 69.849 | 69.849 | **0** |
| C2 | Misma rampa + `kill` del procesador en plena carga | 53.827 | 53.827 | **0** |
| C3 (sola) | 200 ev/s, procesador detenido | 5.990 | 5.990 | **0** |
| C3 (juntos) | 200 ev/s, procesador activo | 5.709 | 5.709 | **0** |
| **Total** | | **135.375** | **135.375** | **0** |

Ninguna corrida tuvo errores HTTP (0,00 %) ni necesitó la contingencia.

### Hallazgo 1 · el procesador era el cuello de botella → paralelismo por partición

| | C1 (secuencial) | C2 (particiones en paralelo) |
|---|---|---|
| Ritmo de drenaje del procesador (1 instancia) | ~90 ev/s | **~343 ev/s (×3,8)** |
| Lag máximo | 61.451 | 46.010 (con el procesador caído ~2,5 min) |
| "Estado visible" p95 al terminar la carga | 107 s | **16,7 s** |
| Tiempo en drenar el atraso tras la carga | 12,7 min | 2,3 min |

Causa: cada mensaje era una transacción con su propio `fsync`, procesados de a uno por instancia. Solución en [ADR-0011](adr/0011-procesamiento-paralelo-por-particion.md).

### Hallazgo 2 · la latencia de ingesta en este entorno la domina la contención de recursos

Mismo tráfico (200 ev/s, 30 s), la API sola frente a la API compitiendo con el procesador:

| | Mediana | p95 | p99 | CPU a mitad de corrida |
|---|---|---|---|---|
| API sola | **25,5 ms** | 163 ms | 287 ms | api 122 %, postgres 22 %, kafka-1 45 % |
| API + procesador | 229 ms | 793 ms | 1.261 ms | procesador 126 %, postgres 112 %, api 75 % |

Las trazas confirman que, dentro de la API, el tiempo es casi todo la confirmación `acks=all` de Kafka (mediana 14 ms en el servidor), con tres brokers replicando dentro de la misma máquina virtual. **El SLO de ingesta (p99 < 200 ms) no se cumple en este portátil** y los umbrales de k6 se dejaron en el valor del SLO a propósito (fallan en rojo, no se maquillan).

### Qué haría falta para certificar 5.000 ev/s

1. Brokers, PostgreSQL y servicios en máquinas separadas (o el Kafka administrado del plan), con la carga generada desde otra máquina.
2. Escalar el procesador por lag (KEDA) y medir la curva instancias → ev/s hasta 24.
3. Si PostgreSQL se vuelve el límite: micro-lotes por partición en el procesador (varias filas por transacción) y proyección de lecturas en Redis.
4. Repetir las corridas de la Fase 2.2: sostenido 5.000 ev/s × 30 min, ráfaga 10.000 ev/s × 5 min, soak 2.000 ev/s × 2 h, y caos con Toxiproxy, **siempre cerrando con reconciliación en cero**.
