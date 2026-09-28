# TCC · Plataforma de eventos de guías

Sistema de alta concurrencia que **captura** eventos de estado de guías desde varios sistemas, **actualiza el estado** casi en tiempo real y **notifica** al cliente, **sin perder eventos y sin degradar la operación** cuando algo falla.

Prueba técnica para *Desarrollador Advance* · implementación en **.NET 10** (la versión Java/Spring Boot usa los mismos contratos, tópicos y migraciones).

## Cómo revisar esta prueba en 10 minutos

| # | Qué | Dónde |
|---|---|---|
| 1 | **Presentación** de los 3 ejes: vista general con diagramas y versión técnica de cada uno (acceso con las credenciales enviadas por correo) | [https://yeissonvj.github.io/prueba-tcc-envio/presentacion/](https://yeissonvj.github.io/prueba-tcc-envio/presentacion/) |
| 2 | **Resultados** medidos: cero pérdida, resiliencia, latencia, calidad | [Resultados en una mirada](#resultados-en-una-mirada) (abajo) |
| 3 | **Arquitectura y decisiones**: componentes, garantías y 12 ADR | [docs/arquitectura.md](docs/arquitectura.md) · [docs/adr](docs/adr/README.md) |
| 4 | **Código**: hexagonal en 7 proyectos .NET | [dotnet/src](dotnet/src) |
| 5 | **Pruebas**: 151 automatizadas, carga, caos y reconciliación | [docs/pruebas.md](docs/pruebas.md) |

**Construido y verificado:** la solución completa en .NET 10 (API, procesador, notificador), probada en local y contra Kafka y PostgreSQL administrados en Aiven.
**Diseño:** la versión Java/Spring Boot del Eje 2 (estructura, equivalencias y código de referencia), que reutiliza los mismos contratos, tópicos y migraciones.
**En la sustentación:** demostración en vivo con el [panel de pruebas](herramientas/panel-pruebas/LEEME.md) (carga, brokers caídos, PostgreSQL, Redis, SMS) y la API publicada temporalmente.

```text
  TMS / Transporte (token OAuth2)
            |
            | POST /api/v1/eventos-guia  ->  202 Accepted
            v
  +--------------------+   Kafka caído   +------------------------+
  |  API de ingesta    | --------------> | contingencia           |
  |  valida, autoriza  |                 | (PostgreSQL)           |
  +--------------------+                 +------------------------+
            | acks=all                               | relay (cuando Kafka vuelve)
            v                                        |
  +--------------------------------+                 |
  | Kafka: guias.eventos.recibidos | <---------------+
  +--------------------------------+
            |
            v
  +--------------------+          +----------------------------------+
  | Procesador         | -------> | PostgreSQL                       |
  | inbox/estado/outbox|  1 tx    | guias, historial, bandeja_salida | <--- GET /api/v1/guias (API)
  +--------------------+          +----------------------------------+
                                                   | relay
                                                   v
                                  +--------------------------------+
                                  | Kafka: guias.estados.cambiados |
                                  +--------------------------------+
                                                   |
                                                   v
  +--------------------------+           +--------------------+
  | reintentos 1m / 10m / 1h | <-------> | Notificador        | ---> SMS / correo al cliente
  |                          |           | SMS -> correo      |
  +--------------------------+           +--------------------+
```

## Resultados en una mirada

| | |
|---|---|
| **Cero pérdida** | 135.375 eventos aceptados en 4 corridas de carga = 135.375 en la base, **incluida una con el procesador eliminado (`kill`) en plena carga** |
| **Exactamente un efecto** | 50 procesamientos concurrentes del mismo evento → 1 fila; replay completo del notificador → 0 SMS repetidos |
| **Resiliencia verificada** | Kafka caído (contingencia + circuito), PostgreSQL caído (reintento bloqueante sin desorden), SMS caído (escalera → correo), poison pills (DLQ), bug real recuperado con replay |
| **Latencia** | 202 → estado visible en ~0,4 s sin carga; primera petición tras arrancar 357 ms |
| **Calidad** | 151 pruebas (unitarias, contrato OpenAPI, arquitectura e integración con Testcontainers), 0 avisos |
| **Hallazgo de capacidad** | El procesador era el cuello de botella (~90 ev/s por instancia) → paralelismo por partición (~343 ev/s, ×3,8). Detalle y límites del entorno en [`docs/pruebas.md`](docs/pruebas.md) |

## Cómo ejecutarlo

**Requisitos:** Docker Desktop (WSL 2) · .NET SDK 10.0.401 (solo para desarrollo) · Git Bash o Linux para los scripts.

### Con un doble clic (Windows)
`iniciar-local.bat` levanta Docker Desktop si hace falta, todo el entorno local (Kafka, PostgreSQL, Redis, Keycloak, observabilidad y aplicaciones) y el panel de pruebas, y abre el navegador. `detener-local.bat` lo apaga conservando los datos.

### Todo el sistema en contenedores
```bash
docker compose -f infra/docker-compose.yml --profile aplicaciones up -d --build
API=http://localhost:8090 ./infra/pruebas/humo.sh
```
La prueba de humo pide un token a Keycloak, envía un evento (202), lo repite (200), envía uno inválido (400) y verifica que el estado aparece por la consulta.

### Desarrollo (Visual Studio / `dotnet run`)
```bash
docker compose -f infra/docker-compose.yml up -d      # solo infraestructura
dotnet run --project dotnet/src/TccEventos.Api --launch-profile http
dotnet run --project dotnet/src/TccEventos.Procesador
dotnet run --project dotnet/src/TccEventos.Notificador
```
Peticiones listas en [`TccEventos.Api.http`](dotnet/src/TccEventos.Api/TccEventos.Api.http) (primero la petición 0, que obtiene el token) y documentación interactiva en http://localhost:5013/scalar/v1.

### Pruebas
```bash
dotnet test dotnet/TccEventos.slnx                     # 151 pruebas, incluye Testcontainers
```
Carga y reconciliación: ver [`docs/pruebas.md`](docs/pruebas.md).

**Pruebas manuales con botones** (carga, brokers caídos, PostgreSQL, Redis, SMS, kill del procesador) contra el entorno local o Aiven:
```bash
python herramientas/panel-pruebas/panel.py            # http://127.0.0.1:8095
```
Ver [`herramientas/panel-pruebas/LEEME.md`](herramientas/panel-pruebas/LEEME.md).

## API

| Método y ruta | Alcance | Respuestas |
|---|---|---|
| `POST /api/v1/eventos-guia` | `eventos:escribir` | **202** durable · **200** duplicado · 400 contrato · 401 · 403 (sin alcance o suplantando otro origen) · 413 · 429 + Retry-After · **503 + Retry-After** (nada durable) |
| `GET /api/v1/guias/{numeroGuia}` | `guias:leer` | 200 estado + historial · 400 · 401 · 403 · 404 · 429 |
| `GET /salud/viva` · `GET /salud/lista` | — | Vida (sin dependencias) · Lista (puede guardar de forma durable en algún lado) |

```json
{
  "idEvento": "0199a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5b",
  "numeroGuia": "TCC123456789",
  "estado": "EN_REPARTO",
  "ocurridoEn": "2026-11-30T10:15:00-05:00",
  "origen": "TMS",
  "novedad": null
}
```

## Servicios locales

| Servicio | URL | |
|---|---|---|
| API (contenedor / desarrollo) | http://localhost:8090 · http://localhost:5013 | |
| Keycloak | http://localhost:8081 | clientes `tms`, `transporte`, `portal-consulta` (secretos solo locales en `infra/keycloak/tcc-realm.json`) |
| Grafana | http://localhost:3000 | tablero "TCC · Plataforma de eventos de guías" (métricas, SLO y logs de Loki enlazados a Jaeger) |
| Jaeger | http://localhost:16686 | trazas de punta a punta |
| Prometheus | http://localhost:9090 | métricas y 6 alertas por SLO |
| Kafka UI | http://localhost:8080 | tópicos, mensajes, DLQ y lag |

Todos los puertos se publican solo en `127.0.0.1`.

## Estructura

```
dotnet/
  src/  Dominio · Aplicacion · Contratos · Infraestructura · Api · Procesador · Notificador
  tests/  Dominio · Aplicacion · Api · Procesador (Testcontainers) · Notificador · Arquitectura
  Dockerfile              una imagen chiseled por servicio (ARG PROYECTO)
db/migraciones/           SQL versionado (Flyway), compartido con la versión Java
infra/                    docker-compose, Kafka, Keycloak, observabilidad, prueba de humo
pruebas-carga/            k6 y reconciliación
.github/                  pipeline (GitHub Actions)
docs/                     arquitectura, ADR, operación, pruebas, liderazgo, visión, IA
```

## CI/CD
[`.github/workflows/ci.yml`](.github/workflows/ci.yml): compilación con avisos como errores y 151 pruebas → NuGet vulnerables y Trivy → imágenes escaneadas con Trivy (publicadas en GHCR solo desde `main`) → prueba de humo con esas imágenes → staging → producción con aprobación manual y **congelamiento en temporada pico**. Validado localmente con actionlint y Trivy.

## Acceso a la presentación

La presentación tiene un login (`docs/presentacion/login.html`) con dos roles: **evaluador** (vistas generales) y **administrador** (además, las versiones técnicas). Las cuentas se crean con:

```bash
python herramientas/credenciales-presentacion.py      # pide usuario y contraseña de cada rol
```

`docs/presentacion/acceso.js` guarda solo hashes PBKDF2-SHA256 con sal, nunca las contraseñas. Es un sitio estático y público: el login ordena la experiencia, pero no es un control de seguridad (el contenido también se puede leer en el repositorio).

## Documentación

| Documento | Contenido |
|---|---|
| [Arquitectura](docs/arquitectura.md) | Componentes, garantías, fallas, datos, patrones, escalabilidad, seguridad |
| [Decisiones (ADR)](docs/adr/README.md) | 12 decisiones con contexto, consecuencias y alternativas |
| [Operación](docs/operacion.md) | Manual de guardia: alertas, DLQ, replay, reconciliación |
| [Pruebas](docs/pruebas.md) | Pirámide, caos, carga y reconciliación con resultados reales |
| [Liderazgo](docs/liderazgo.md) · [Visión](docs/vision.md) · [IA](docs/ia.md) | Equipo, 90 días, 18 meses y agentes |
| [Presentación](docs/presentacion/index.html) | Página de inicio con los 3 ejes; cada eje con vista general (diagramas), versión técnica y explicación narrada |
| [Demo en Aiven](docs/demo-aiven.md) | Kafka y PostgreSQL administrados (plan gratuito) con TLS |
| [AGENTS.md](AGENTS.md) | Reglas para agentes de código y personas nuevas |

## Limitaciones conocidas
- **Capacidad no certificada:** las pruebas de carga corrieron en un portátil que comparte CPU entre todos los componentes; el SLO de ingesta (p99 < 200 ms) no se cumple ahí. Qué haría falta: [`docs/pruebas.md`](docs/pruebas.md#qué-haría-falta-para-certificar-5000-evs).
- **Demo en la nube (Aiven, plan gratuito):** verificada ([guía y resultados](docs/demo-aiven.md)); el plan gratuito limita tópicos, particiones y throughput, así que es una demo funcional, no de capacidad. Publicación de imágenes en GHCR: automática al integrar en `main`.
- **Proyector de lecturas en Redis, integraciones y agente de novedades:** diseñados, fuera del alcance del ejercicio.
- **Versión Java/Spring Boot:** diseñada ([Eje 2, versión técnica](docs/presentacion/eje2-tecnica.html)); la implementación es el siguiente paso.
