# Guía para agentes de código (y personas nuevas)

Contexto del repositorio en [`README.md`](README.md) y [`docs/arquitectura.md`](docs/arquitectura.md).

## Reglas
- **Nombres en español sin tildes ni eñes** en código, tópicos, tablas y columnas ([ADR-0012](docs/adr/0012-nombres-en-espanol-y-diseno-portable.md)).
- **Arquitectura hexagonal:** `Dominio` no depende de nada; `Aplicacion` solo de `Dominio`; `Infraestructura` nunca de un host. Lo verifican `TccEventos.Arquitectura.Pruebas`.
- **Diseño portable a Java/Spring:** no introducir librerías exclusivas de .NET (MediatR, FluentValidation, EF Migrations, user-secrets).
- **Esquema:** solo con una nueva migración `db/migraciones/V{n}__*.sql`, compatible con la versión anterior (expand/contract). Nunca editar una migración ya aplicada.
- **Contratos (`Contratos/V1`):** cambios solo compatibles hacia atrás (campos opcionales nuevos). La prueba de contrato OpenAPI falla ante cambios accidentales.
- **Kafka:** publicar siempre con `ProductorKafka` y consumir con `ConsumidorKafka` (durabilidad, offsets y trazas en un solo lugar).
- **Pruebas:** xUnit con `Assert` nativo y falsos escritos a mano; integración con Testcontainers; toda corrección de bug trae su prueba.
- **Sin secretos ni datos reales** en código, pruebas o prompts. Los secretos de `infra/keycloak/tcc-realm.json` son solo locales.
- **Archivos UTF-8 sin BOM.** En Windows PowerShell 5.1 no reescribir archivos con `Get-Content`/`Set-Content` (corrompe las tildes); usar el editor o .NET con UTF-8 explícito.

## Antes de abrir un PR
```bash
dotnet build dotnet/TccEventos.slnx -warnaserror
dotnet test dotnet/TccEventos.slnx
docker compose -f infra/docker-compose.yml --profile aplicaciones up -d --build
API=http://localhost:8090 ./infra/pruebas/humo.sh
```
Quien abre el PR responde por el código, lo haya escrito una persona o un agente.
