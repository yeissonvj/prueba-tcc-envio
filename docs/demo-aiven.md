# Demo con Kafka y PostgreSQL administrados (Aiven, plan gratuito)

Demuestra que el sistema funciona igual sobre un broker y una base administrados en la nube, conectándose con TLS. Las aplicaciones, Keycloak y Redis corren en tu máquina.

## Qué cambia respecto al entorno local

| Aspecto | Local | Aiven gratuito | Cómo se adapta (solo configuración) |
|---|---|---|---|
| Tópicos | 7 (24/6/3 particiones) | **5 tópicos × 2 particiones** | Una sola etapa de reintento de notificaciones (`Notificador__EtapasActivas=1`) |
| Conexión a Kafka | PLAINTEXT | **TLS**: certificado de cliente o SASL/SCRAM | `Kafka__ProtocoloSeguridad` y certificados ([`OpcionesKafka`](../dotnet/src/TccEventos.Infraestructura/Kafka/OpcionesKafka.cs)) |
| Réplicas sincronizadas mínimas | 2 | Según el plan | `AIVEN_KAFKA_MIN_ISR` (por defecto 1) |
| Conexiones a PostgreSQL | Sin límite práctico | **Máximo 20** | `Maximum Pool Size=5` por servicio |
| Throughput | Lo que dé la máquina | **250 KiB/s de entrada y salida** | Demo funcional, no de carga |
| Disponibilidad | Siempre | Se apaga si no hay actividad | Encenderlo en la consola antes de la demo |

Lo que se encontró al integrar (servicios gratuitos creados en septiembre de 2026):

| Hallazgo | Detalle |
|---|---|
| Kafka | 2 brokers; tópicos con `ReplicationFactor: 2` y `min.insync.replicas=1` (el diseño local usa 3 y 2: con un broker caído, Aiven gratuito confirma con una sola copia) |
| PostgreSQL | Versión 18.6 (local: 17; las migraciones son SQL estándar y se aplicaron sin cambios), `max_connections=20` |
| TLS | Kafka con certificado de cliente y PostgreSQL con `SSL Mode=VerifyFull` contra el `ca.pem` del proyecto |
| Kerberos | Npgsql intenta cifrado GSS por defecto y la imagen chiseled no trae `libgssapi`: se desactiva con `GSS Encryption Mode=Disable` |

Límites tomados de la documentación de Aiven: [Kafka free tier](https://aiven.io/docs/products/kafka/free-tier/kafka-free-tier) y [PostgreSQL free tier](https://aiven.io/docs/products/postgresql/concepts/pg-free-tier).

## Pasos

### 1. En la consola de Aiven (lo hace la persona dueña de la cuenta)
1. Crear la cuenta en https://console.aiven.io.
2. Crear un servicio **Apache Kafka · plan Free** y un servicio **PostgreSQL · plan Free**, preferiblemente en la misma región (p. ej. AWS us-east).
3. En el servicio de Kafka, pestaña **Overview**: descargar **CA certificate** (`ca.pem`), **Access certificate** (`service.cert`) y **Access key** (`service.key`) en `infra/aiven/certificados/`.
4. Copiar `infra/aiven/.env.ejemplo` como `infra/aiven/.env` y completar el host y puerto de Kafka y el host, puerto y contraseña de PostgreSQL.

> `infra/aiven/.env` y los certificados están en `.gitignore`: nunca se suben al repositorio.

### 2. Crear los 5 tópicos
```bash
./infra/aiven/crear-topicos.sh
```
Si el plan no permite crear tópicos por la API de Kafka, crearlos en la pestaña **Topics** de la consola con esos mismos nombres y 2 particiones: `guias.eventos.recibidos`, `guias.estados.cambiados`, `guias.eventos.dlq`, `notificaciones.reintento.1m`, `notificaciones.dlq`.

### 3. Levantar el sistema contra Aiven
Detener antes el entorno local (usa los mismos puertos de Keycloak):
```bash
docker compose -f infra/docker-compose.yml --profile aplicaciones down
docker compose -f infra/aiven/docker-compose.yml up -d --build
```
Flyway aplica las migraciones en el PostgreSQL de Aiven antes de arrancar las aplicaciones.

### 4. Probar
```bash
API=http://localhost:8091 SEGUNDOS_MAXIMOS=20 ./infra/pruebas/humo.sh
```
Opcional, carga moderada dentro del límite de 250 KiB/s:
```bash
docker run --rm --network tcc-eventos-aiven_default -v "$PWD/pruebas-carga:/pruebas" \
  -e CORRIDA=AIVEN1 -e ETAPAS=50:60s grafana/k6:2.3.0 run /pruebas/k6/ingesta.js
```
La reconciliación del entorno local (`reconciliar.sh`) lee la base local; en Aiven se verifica con la consulta `SELECT count(*) FROM guias WHERE numero_guia LIKE 'AIVEN1%'` desde la consola de Aiven.

### Resultados obtenidos

| Prueba | Resultado |
|---|---|
| Prueba de humo | 401 / 202 / 200 (duplicado) / 400 correctos; estado visible por la consulta en **3,1 s** |
| Carga k6 (`AIVEN1`, 50 ev/s × 60 s) | 3.000 aceptados, **0 errores**; ingesta mediana 93 ms, **p99 114 ms** (SLO < 200 ms) |
| Reconciliación | **3.000 aceptados = 3.000 en el PostgreSQL de Aiven, diferencia 0** |

**Hallazgo:** el procesador drenó a unos 5 ev/s y tardó ~9 minutos en procesar la carga. No es pérdida ni falla: en esta demo el procesador corre en el equipo local y PostgreSQL en la región de Aiven, a ~90 ms por viaje, y cada evento hace unos 5 viajes a la base (inbox, lectura de la guía y la transacción) con solo 2 particiones en paralelo. En producción el procesador corre en la misma región que la base (~1 ms por viaje). Si hubiera que operar con la base lejos, la mejora sería reducir viajes: unir la verificación del inbox con la lectura de la guía y procesar en micro-lotes por partición ([ADR-0011](adr/0011-procesamiento-paralelo-por-particion.md)).

### 5. Apagar
```bash
docker compose -f infra/aiven/docker-compose.yml down
```
Los servicios de Aiven se apagan solos por inactividad; también se pueden apagar o eliminar desde la consola.

## Acceso público temporal con ngrok (opcional)

Por defecto todo se publica solo en `127.0.0.1`. Para que alguien externo pruebe la API se levanta el perfil `publico`: un nginx (`infra/aiven/publico/nginx.conf`) detrás de un único túnel de ngrok.

```text
Internet ──https──► ngrok ──► proxy-publico (nginx) ──► api:8080          /api/*, /salud/*
                                                    └─► keycloak:8081     solo POST /realms/tcc/protocol/openid-connect/token
                                                        (todo lo demás → 404: consola de administración inaccesible)
```

1. Crear una cuenta en ngrok y copiar el authtoken en `infra/aiven/.env` como `NGROK_AUTHTOKEN=...` (no se versiona).
2. `docker compose -f infra/aiven/docker-compose.yml --profile publico up -d`
3. La URL pública aparece en http://127.0.0.1:4040 o con `docker compose -f infra/aiven/docker-compose.yml logs ngrok | grep url=`.
4. Probar desde cualquier lugar: `API=https://<url> TOKEN_URL=https://<url>/realms/tcc/protocol/openid-connect/token ./infra/pruebas/humo.sh`
5. Cerrar al terminar: `docker compose -f infra/aiven/docker-compose.yml stop ngrok proxy-publico`

**Seguridad:** los secretos de los clientes de `tcc-realm.json` son de desarrollo y están en el repositorio público; con el túnel abierto, cualquiera que tenga la URL podría obtener un token y escribir eventos en la demo. Mantener el túnel abierto solo durante la prueba, o regenerar los secretos de los clientes en Keycloak antes de compartir la URL. El límite de peticiones por cliente de la API sigue activo.

Verificado a través del proxy: token emitido, evento `202`, repetido `200`, sin token `401`, `/admin` y el resto de Keycloak `404`.
