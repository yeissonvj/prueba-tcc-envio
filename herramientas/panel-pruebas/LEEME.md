# Panel de pruebas manuales

Botones para lanzar a mano las mismas pruebas que se hicieron por consola: carga con reconciliación, brokers caídos, PostgreSQL, Redis, procesador y proveedor de SMS. Muestra el registro en vivo y el resultado.

```bash
python herramientas/panel-pruebas/panel.py      # luego abrir http://127.0.0.1:8095
```

Requisitos: Python 3.10+ (sin dependencias) y Docker. Funciona contra el entorno que esté levantado:

| Entorno | Cómo levantarlo | Botones disponibles |
|---|---|---|
| Local (3 brokers) | `docker compose -f infra/docker-compose.yml --profile aplicaciones up -d` | Todos |
| Aiven | `docker compose -f infra/aiven/docker-compose.yml up -d` | Carga, procesador, Redis, SMS (Kafka y PostgreSQL son administrados: no se pueden apagar) |

## Qué hace cada botón

| Botón | Qué ejecuta | Qué se espera |
|---|---|---|
| Prueba de carga | k6 a tasa constante contra la API y luego reconciliación (`guias` en PostgreSQL vs. respuestas 202) | Diferencia 0 |
| … con kill del procesador | `docker kill` del procesador a mitad de la carga y `docker start` 15 s después | Diferencia 0 |
| 1 broker caído | `docker stop kafka-3` | 202 desde Kafka, salud Healthy |
| 2 brokers caídos | `docker stop kafka-3 kafka-2` (se pierde el quórum de KRaft) | 202 desde la contingencia en PostgreSQL, salud Degraded |
| Kafka completo | los 3 brokers detenidos | 202 desde la contingencia |
| Recuperar Kafka | inicia los brokers y espera a que la contingencia quede vacía | Contingencia en 0 y guías visibles |
| PostgreSQL | `docker stop postgres` | 202 (Kafka guarda); el estado avanza al recuperar |
| Procesador | detiene o inicia el procesador | 202 y lag acumulado; drena al iniciarlo |
| Redis | `docker stop redis` | 202; un duplicado lo descarta el inbox |
| Proveedor de SMS | recrea el notificador con `SIMULAR_SMS_CAIDO=true` y la escalera de reintentos en 5/10/15 s | Los avisos salen por correo |
| Eventos de prueba | N eventos `EN_REPARTO` uno a uno, un reenvío duplicado, espera del estado visible y de los avisos | 202, 200 en el duplicado, avisos por SMS o correo |

Las corridas de carga se guardan en `pruebas-carga/resultados/PANEL*` (ignoradas por git).

## Seguridad

El panel controla Docker en esta máquina. Por eso:
- escucha solo en `127.0.0.1` y **nunca** debe publicarse (no pasa por el proxy de ngrok);
- rechaza peticiones con otro `Host` u `Origin` (otra página abierta en el navegador no puede disparar acciones);
- lee los secretos del disco (`infra/aiven/.env`, `infra/keycloak/tcc-realm.json`) y no los envía al navegador ni al registro.
