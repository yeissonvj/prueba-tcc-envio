# Inteligencia artificial y agentes

**Principio:** la IA nunca está en el camino crítico. Si un agente falla o se equivoca, la ingesta, el estado y las notificaciones siguen funcionando.

## En la operación

| Prioridad | Agente | Qué hace |
|---|---|---|
| 1 | **Novedades** | Consume `guias.estados.cambiados` (estado `NOVEDAD`, grupo propio `ai-novelty`), clasifica la causa (dirección, ausencia, rechazo) y propone o ejecuta la acción |
| 2 | Guías estancadas | Detecta guías sin movimiento anómalo y alerta antes de que el cliente reclame |
| 3 | Asistente de DLQ | Agrupa los mensajes de la DLQ por causa y sugiere el redrive o la corrección |

Reglas de diseño:
- **Sus acciones entran como eventos por la misma API** (origen `AI_AGENT`, con su propio cliente OAuth2 y alcance): quedan en el historial y pasan por las mismas validaciones que cualquier sistema.
- **Autonomía según el riesgo:** bajo (reprogramar una visita) → automático; alto (devolver un envío) → lo aprueba un operador.
- Herramientas expuestas vía **MCP**; datos personales enmascarados (Ley 1581); salida estructurada y validada contra un esquema.
- Conjunto de evaluación versionado; modelos pequeños para clasificar, grandes solo donde aportan.
- Métricas de negocio, no de modelo: entregas exitosas en segundo intento, tiempo de resolución de novedades, % de sugerencias aceptadas.

## En la construcción

Uso: andamiaje, pruebas, migración .NET → Java, revisión de PR y documentación.

Reglas del equipo (ver [`AGENTS.md`](../AGENTS.md)):
- Las mismas compuertas para el código generado que para el escrito a mano (pruebas, arquitectura, contrato, seguridad).
- **Quien abre el PR responde por el código**, lo haya escrito una persona o un agente.
- Nada de secretos ni datos reales en los prompts.
- Se miden resultados (lead time, fallas en producción), no líneas generadas.

**Riesgo principal:** personas junior aceptando código que no entienden. Mitigación: *pairing*, revisiones que piden explicar el porqué y postmortems sin culpables.

**Experiencia en este repositorio:** el desarrollo se hizo con un agente de código trabajando paso a paso. Varias de las decisiones más valiosas salieron de **verificar contra el sistema real** lo que las pruebas en memoria no mostraban (límite de cuerpo ignorado, IPv6 vs IPv4, fechas UTC, el cuello de botella del procesador): el agente acelera, pero la verificación sigue siendo obligatoria.
