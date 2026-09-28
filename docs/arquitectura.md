# Arquitectura · Plataforma de eventos de guías

> Cómo está construido el sistema y por qué. Las decisiones puntuales están en [`adr/`](adr/); la operación diaria en [`operacion.md`](operacion.md).

## 1. El problema

En temporada pico el volumen de eventos de guías (estados, novedades, notificaciones) se multiplica. Se necesita un sistema que **capture** eventos de muchos sistemas, **actualice el estado** casi en tiempo real y **notifique** al cliente, **sin perder eventos, sin degradar la operación** y con un diseño que el equipo pueda sostener.

### Supuestos de volumen (no son cifras reales de TCC)

| Variable | Supuesto |
|---|---|
| Guías/día en pico (×4 sobre lo normal) | 2,4 millones |
| Eventos por guía | 8 → ~19,2 millones de eventos/día |
| Hora pico / ráfagas | ~1.350 ev/s / 5.000 ev/s |
| **Objetivo de diseño** | **5.000 ev/s sostenidos; pruebas a 10.000 ev/s** |
| Particiones | 24 (≈ 500 ev/s por partición, margen ×2) |

### SLO

| Requisito | Meta | Cómo se mide |
|---|---|---|
| Durabilidad | 0 eventos perdidos: 202 solo si el evento es durable | Reconciliación (`pruebas-carga/reconciliar.sh`) |
| Latencia de ingesta | p99 < 200 ms | `http_server_request_duration_seconds` |
| Estado visible | p95 < 5 s | `tcc_latencia_estado_seconds` |
| Notificación | p95 < 60 s | `tcc_latencia_notificacion_seconds` |
| Duplicados | Nunca doble efecto | Inbox + clave de notificación |
| Orden | Garantizado por guía | Clave de partición = número de guía |
| Degradación | Si cae notificaciones, ingesta y rastreo siguen | Grupos de consumo independientes |

## 2. Contexto

```text
  TMS ------------------------+                                  +---> Clientes (SMS / correo)
  Transporte (mensajeros) ----+     +-----------------------+    +---> Rastreo web (portal)
  Internacional --------------+---> | Plataforma de eventos |----+---> Gestión documental
  Automatización -------------+     | de guías              |    +---> Agentes IA (novedades)
                                    | captura, estado,      |
                                    | notificación          |
                                    +-----------------------+
```

## 3. Componentes

```text
  Keycloak (tokens OAuth2) ....> API de ingesta

  +----------------------------------------------+
  | API de ingesta                               |---> Redis: filtro de duplicados (72 h)
  | valida · autoriza · publica · responde 202   |---> PostgreSQL: contingencia_eventos
  +----------------------------------------------+          |  (solo si Kafka no confirma)
                         | acks=all                         |  relay cuando Kafka vuelve
                         v                                  |
  +----------------------------------------------+          |
  | Kafka: guias.eventos.recibidos               |<---------+
  | 24 particiones · clave = número de guía      |
  +----------------------------------------------+
                         |
                         v
  +----------------------------------------------+
  | Procesador de estado                         |---> guias.eventos.dlq (mensajes ilegibles)
  | inbox · máquina de estados · outbox          |
  +----------------------------------------------+
                         | una sola transacción
                         v
  +----------------------------------------------+
  | PostgreSQL                                   |<--- GET /api/v1/guias (API)
  | guias · historial_eventos · bandeja_salida   |
  +----------------------------------------------+
                         | relay con un solo líder
                         v
  +----------------------------------------------+
  | Kafka: guias.estados.cambiados               |...> proyector, integraciones, agente IA
  | 24 particiones                               |     (fuera del alcance del ejercicio)
  +----------------------------------------------+
                         |
                         v
  +----------------------------------------------+
  | Notificador                                  |<--> notificaciones.reintento.1m / .10m / .1h
  | SMS -> correo · reintentos · circuito        |---> notificaciones.dlq
  +----------------------------------------------+
```

| Componente | Responsabilidad | Por qué así |
|---|---|---|
| **API de ingesta** | Valida el contrato, autentica y autoriza, publica en Kafka y responde 202 | Sin base de datos en el camino normal: escala horizontalmente sin límite |
| Redis | Descarta duplicados obvios (72 h) | **Optimización, no garantía**: si cae, la API sigue ([ADR-0004](adr/0004-filtro-duplicados-tolerante.md)) |
| Contingencia en PostgreSQL | Guarda lo que Kafka no confirma; un relay lo reenvía | Cero pérdida con el broker caído ([ADR-0002](adr/0002-ingesta-directa-a-kafka-con-contingencia.md)) |
| `guias.eventos.recibidos` | Registro durable de todo lo recibido | Replay ante errores (se usó de verdad: ver [pruebas](pruebas.md)) |
| **Procesador de estado** | Inbox + máquina de estados + outbox en una transacción | Única fuente de verdad del estado |
| Relay de `bandeja_salida` | Publica los cambios confirmados en `guias.estados.cambiados` | Evita la doble escritura inconsistente; un solo líder conserva el orden ([ADR-0006](adr/0006-relay-outbox-con-un-solo-lider.md)) |
| **Notificador** | Envía SMS (correo como alterno) con reintentos no bloqueantes | Una falla del proveedor no frena nada más ([ADR-0005](adr/0005-reintentos-bloqueantes-vs-no-bloqueantes.md)) |
| Keycloak | Emite tokens a cada sistema emisor | Seguridad fuera del código de negocio ([ADR-0008](adr/0008-seguridad-oauth2-y-autorizacion-por-origen.md)) |
| Observabilidad | OpenTelemetry → Collector → Jaeger (trazas), Prometheus (métricas y alertas), Loki (logs); Grafana para consultar | Las apps solo conocen OTLP ([ADR-0009](adr/0009-observabilidad-opentelemetry.md)) |

## 4. Recorrido de un evento y sus garantías

```text
     Emisor         API         Kafka      Procesador    PostgreSQL    Notificador     Cliente
        |            |            |             |             |             |             |
  1     |----------->|            |             |             |             |             |    POST /api/v1/eventos-guia (JWT)
  2     |            |*           |             |             |             |             |    valida contrato, alcance y origen
  3     |            |----------->|             |             |             |             |    publica (acks=all, idempotente, clave = guía)
  4     |            |<-----------|             |             |             |             |    confirmado por al menos 2 réplicas
  5     |<-----------|            |             |             |             |             |    202 Accepted (Location: /api/v1/guias/{n})
  6     |            |            |------------>|             |             |             |    entrega (partición de la guía)
  7     |            |            |             |------------>|             |             |    BEGIN · historial (inbox) · guía (versión) · bandeja_salida · COMMIT
  8     |            |            |<------------|             |             |             |    marca el offset (solo después del COMMIT)
  9     |            |            |<--------------------------|             |             |    relay publica en guias.estados.cambiados
 10     |            |            |---------------------------------------->|             |    entrega el cambio
 11     |            |            |             |             |             |*            |    ¿notifica este estado? ¿ya se envió? ¿es obsoleto?
 12     |            |            |             |             |             |------------>|    SMS / correo al cliente
```

| Paso | Garantía |
|---|---|
| Validación y autorización en la frontera | Nada inválido ni suplantado entra al sistema |
| `acks=all` + `min.insync.replicas=2` + productor idempotente | Sin 202 no hay durabilidad; con 202, hay al menos 2 copias |
| Inbox (`historial_eventos.id_evento` PK) | Un evento repetido nunca produce doble efecto |
| Historial + estado + outbox en una transacción | Todo o nada |
| Offset marcado después del COMMIT | Si el proceso cae, se relee y el inbox descarta |
| Clave `guía:versión:canal` en notificaciones | Nunca dos veces la misma notificación |

**Al menos una vez (Kafka) + idempotencia (inbox y claves) = efecto exactamente una vez.**

## 5. Configuración de durabilidad

| Extremo | Configuración |
|---|---|
| Productor (`ProductorKafka`, único para todo el sistema) | `acks=all`, `enable.idempotence=true`, `max.in.flight=5`, `delivery.timeout.ms=5000`, `linger.ms=5`, `lz4` |
| Broker (3 nodos KRaft) | `replication.factor=3`, `min.insync.replicas=2`, `unclean.leader.election.enable=false`, creación automática de tópicos desactivada |
| Consumidor (`ConsumidorKafka`) | `enable.auto.offset.store=false` + `StoreOffset` tras procesar + commit automático en lote; `cooperative-sticky` |

**Regla de oro:** nunca 202 sin almacenamiento durable. Si ni Kafka ni la contingencia guardan, la API responde **503 + Retry-After** y el emisor conserva el evento.

## 6. Escenarios de falla (verificados)

| Falla | Resultado | Evidencia |
|---|---|---|
| Emisor repite un evento | 200 sin efecto | Pruebas de endpoint y `humo.sh` |
| Kafka caído | 202 desde contingencia; el circuito evita esperar 5 s por petición; el relay reenvía al volver | Prueba de caos 5.5 |
| Kafka y PostgreSQL caídos | 503 inmediato | Prueba de caos 5.5 |
| 1 de 3 brokers caído | Sigue sano (ISR ≥ 2) | Prueba de sondas 5.6 |
| 2 de 3 brokers caídos | `Degraded`: opera en contingencia | Prueba de sondas 5.6 |
| PostgreSQL caído con eventos llegando | Procesador reintenta bloqueando la partición; nada se salta ni se desordena | Prueba de caos 6.5 |
| Procesador eliminado (`kill`) en plena carga | Relectura desde el último offset; el inbox descarta; reconciliación exacta | Corrida C2 ([pruebas](pruebas.md)) |
| Mensaje ilegible (poison pill) | DLQ con motivo y origen; la partición sigue | Prueba de caos 6.5 |
| Bug que falla todos los eventos | 3 intentos → DLQ; replay tras corregir | Prueba de humo 6.3 |
| Proveedor de SMS caído | Reintentos 1 min/10 min/1 h → correo → DLQ | Prueba de caos 7.5 |
| Redis caído | Sin filtro; el inbox deduplica | Pruebas del decorador |

## 7. Datos

Tablas en español (igual que el código), creadas por **migraciones SQL versionadas que ejecuta Flyway** y que comparten .NET y Java ([ADR-0007](adr/0007-migraciones-sql-compartidas-con-flyway.md)):

| Tabla | Rol |
|---|---|
| `guias` | Estado actual; `version` = concurrencia optimista y orden de los cambios |
| `historial_eventos` | Historial **e inbox** (`id_evento` PK). Sin particionar a propósito ([ADR-0003](adr/0003-inbox-en-historial-sin-particionar.md)) |
| `bandeja_salida` | Outbox; guarda el contexto de traza para no cortar la traza distribuida |
| `contingencia_eventos` | Buffer de la API cuando Kafka no confirma |
| `notificaciones_enviadas` | Idempotencia y descarte de notificaciones obsoletas |

### Máquina de estados

```text
  Creada
    |
    v
  Recogida
    |
    v
  EnBodegaOrigen
    |
    v
  EnTransito
    |
    v
  EnBodegaDestino
    |
    v
  EnReparto --------------------> Entregada   (final)
    |    ^
    |    +----------------+
    v                     |
  Novedad -----------> ReintentoEntrega
    |
    v
  Devuelta   (final)
```

Además, todo estado no final puede pasar a `Novedad`. Reglas: duplicado → se ignora; más reciente y válido → aplica; tardío → historial `TARDIO`; inválido → historial `TRANSICION_INVALIDA`; estado final → no cambia.

## 8. Tópicos

| Tópico | Particiones | Retención | Propósito |
|---|---|---|---|
| `guias.eventos.recibidos` | 24 | 7 días | Todo lo recibido; replay |
| `guias.estados.cambiados` | 24 | 7 días | Cambios validados (con `version`) |
| `notificaciones.reintento.1m` / `.10m` / `.1h` | 6 c/u | 1 día | Reintentos no bloqueantes |
| `guias.eventos.dlq` / `notificaciones.dlq` | 3 c/u | 30 días | Revisión humana y redrive |

## 9. Arquitectura del código (hexagonal)

```text
       Api              Procesador          Notificador       <- hosts (raíces de composición)
        |                    |                    |
        +--------------------+--------------------+
                             |  los hosts usan Infraestructura, Aplicación y Contratos
                             v
                  +---------------------+       +-------------+
                  |   Infraestructura   |------>|  Contratos  |   EventoGuiaV1, GuiaV1 ...
                  +---------------------+       +-------------+
                             |  implementa los puertos
                             v
                  +---------------------+
                  |     Aplicación      |   casos de uso · puertos · decoradores
                  +---------------------+
                             |
                             v
                  +---------------------+
                  |       Dominio       |   Guia · MaquinaEstados · PoliticaNotificacion
                  +---------------------+
```

Las dependencias se verifican en cada compilación (`TccEventos.Arquitectura.Pruebas`): el dominio no depende de nada, la aplicación solo del dominio, la infraestructura nunca de un host y los hosts no dependen entre sí.

**Patrones usados y dónde:**

| Patrón | Dónde | Para qué |
|---|---|---|
| Puertos y adaptadores | `Aplicacion/Puertos` ↔ `Infraestructura` | Cambiar tecnología sin tocar reglas |
| Decorador | `FiltroDuplicadosTolerante`, `PublicadorConContingencia`, `PublicadorConCircuito`, `ProveedorConCircuito` | Añadir resiliencia sin modificar el adaptador (OCP) |
| Circuit Breaker | Kafka y cada proveedor (Polly) | Fallar rápido ante una caída sostenida |
| Transactional Outbox + Inbox | Procesador | Exactamente un efecto con Kafka "al menos una vez" |
| Cadena de responsabilidad | `IExceptionHandler` de la API | Un manejador por tipo de error |
| Plantilla / Estrategia | `ConsumidorKafka` + manejadores | Un bucle de consumo probado para todos los servicios |
| CQRS ligero | `IConsultaGuias` | Leer sin cargar el agregado |

## 10. Escalabilidad

- **API:** sin estado; escala por CPU/RPS.
- **Procesador y notificador:** escalan por **lag** (KEDA), hasta 24 instancias (una por partición). Dentro de cada instancia, **las particiones se procesan en paralelo** conservando el orden por guía ([ADR-0011](adr/0011-procesamiento-paralelo-por-particion.md)).
- **Lecturas masivas:** hoy PostgreSQL; a escala, proyección en Redis detrás de `IConsultaGuias`.
- Resultados medidos y cuellos de botella: [pruebas.md](pruebas.md).

## 11. Seguridad

- OAuth2 *client credentials* por sistema emisor; JWT RS256 validado (emisor, audiencia, vigencia).
- Alcances: `eventos:escribir` y `guias:leer`; **cada cliente solo reporta su propio `origen`** (anti-suplantación).
- Límite de peticiones por cliente (429); el límite global va en el API Gateway.
- **Ley 1581 de 2012:** los eventos no llevan teléfono ni correo; el notificador los consulta al enviar y los enmascara en los logs.
- Imágenes *chiseled* sin shell y con usuario no root; Trivy en el pipeline; puertos locales solo en `127.0.0.1`; secretos por variables de entorno.

## 12. Ventajas, desventajas y alternativas

| Ventaja | Desventaja | Mitigación |
|---|---|---|
| Cero pérdida demostrable | Más piezas que operar | IaC, entorno local idéntico, Kafka administrado en producción |
| Absorbe picos | Consistencia eventual (segundos) | SLO p95 < 5 s acordado con producto |
| Fallas aisladas | Depuración asíncrona más difícil | Trazas distribuidas de punta a punta |
| Replay | La evolución del contrato puede romper consumidores | Contratos versionados (`V1`) y prueba de contrato OpenAPI |
| Orden por guía sin bloqueos | Techo por número de particiones | 24 particiones + paralelismo por partición |

**Alternativas descartadas:** REST síncrono entre sistemas (fallas en cascada); RabbitMQ (orden por guía y replay más difíciles; válido para el notificador); guardar en base antes de publicar (cuello de botella en el camino crítico); event sourcing completo (excesivo para el equipo).

> Cambiamos simplicidad por resiliencia, conscientemente, solo donde el negocio lo exige.
