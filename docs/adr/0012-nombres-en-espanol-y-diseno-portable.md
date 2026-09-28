# ADR-0012 · Nombres en español y diseño portable a Java/Spring

**Estado:** aceptada

## Contexto
El equipo trabaja en español y el sistema se construye primero en .NET 10 y luego en Java/Spring Boot, el stack corporativo de TCC.

## Decisión
- **Nombres en español sin tildes ni eñes** en código, tópicos, tablas y columnas (`EventoGuia`, `guias.eventos.recibidos`, `historial_eventos`). El contrato JSON va en camelCase (`numeroGuia`) y los estados en mayúsculas del contrato (`EN_REPARTO`).
- **Solo piezas con equivalente directo en Spring:**

| Necesidad | .NET | Spring Boot |
|---|---|---|
| API | Minimal APIs | Spring Web (`@RestController`) |
| Errores | `IExceptionHandler` (cadena) | `@RestControllerAdvice` |
| Validación | Validador propio (sin FluentValidation) | Validador propio o Bean Validation |
| Kafka | Confluent.Kafka | Spring for Apache Kafka |
| Resiliencia | Polly | Resilience4j |
| PostgreSQL | Npgsql (SQL explícito) | JdbcTemplate |
| Migraciones | Flyway (contenedor) | Flyway |
| Seguridad | JwtBearer | oauth2-resource-server |
| Observabilidad | OpenTelemetry .NET | Micrometer / agente OpenTelemetry |
| Configuración | Variables de entorno (sin user-secrets) | Variables de entorno |
| Pruebas | xUnit + Testcontainers, falsos a mano | JUnit 5 + Testcontainers |
| Arquitectura | Pruebas por reflexión | ArchUnit |

## Consecuencias
- La versión Java es una traducción, no un rediseño: mismos contratos (la prueba de contrato OpenAPI es la referencia), mismas migraciones, mismos tópicos.
- Se evitaron a propósito librerías exclusivas de .NET (MediatR, FluentValidation, EF Migrations, user-secrets).
