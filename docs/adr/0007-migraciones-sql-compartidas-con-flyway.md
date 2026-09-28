# ADR-0007 · Migraciones SQL versionadas con Flyway, compartidas por .NET y Java

**Estado:** aceptada

## Contexto
El sistema se implementa en .NET y luego en Java/Spring Boot sobre el mismo esquema. EF Core Migrations solo sirve a .NET y Flyway embebido solo a Java; mantener dos historiales de migración sobre la misma base es una fuente de divergencias.

## Decisión
Archivos SQL planos en `db/migraciones/V{n}__descripcion.sql`, ejecutados por el contenedor oficial de **Flyway** (servicio `migraciones` en Compose; en producción, un job previo al despliegue). Ningún servicio migra al arrancar.

## Consecuencias
- Un solo esquema y un solo historial para ambas implementaciones; Java puede usar Flyway de forma nativa con los mismos archivos.
- Las pruebas de integración aplican **esos mismos archivos** en PostgreSQL desechable: se prueba el esquema real.
- Las migraciones deben ser *expand/contract* (compatibles con la versión anterior) para desplegar sin detener el servicio.
- Tablas y columnas en español, alineadas con el código ([ADR-0012](0012-nombres-en-espanol-y-diseno-portable.md)).

## Alternativas descartadas
- **EF Core Migrations:** exclusivo de .NET.
- **Migrar al arrancar la aplicación:** varias instancias compiten por migrar y un error de migración tumba el despliegue completo.
