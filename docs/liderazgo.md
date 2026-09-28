# Liderazgo técnico y gestión del cambio

> El objetivo no es un sistema que solo entienda quien lo diseñó, sino un equipo que lo opere, lo mejore y no involucione cuando el líder no está.

## 1. Equipo (3–5 personas)
1 senior, 2 intermedios, 1–2 junior. **Propiedad por componente con al menos dos conocedores** (API, procesador, notificador, plataforma) y rotación trimestral. Sin héroes: ningún componente depende de una sola persona.

## 2. Primeros 90 días
Si se inicia en octubre, el mes 3 coincide con la temporada pico → el objetivo realista es **modo sombra en pico**, no reemplazar lo existente.

| Mes | Foco | Entregables |
|---|---|---|
| 1 · Entender | 1:1 con cada persona, mapa de sistemas, qué falló en el último pico, métricas DORA base | Primera victoria temprana en observabilidad |
| 2 · Fundamentos | Estándares acordados, pipeline, *walking skeleton* en staging | Primeros ADR escritos por el equipo |
| 3 · Entregar | Piloto con TMS, prueba de carga, modo sombra en pico, retro | Plan de migración real para enero |

## 3. Empoderamiento
- Todas las personas escriben ADR (ver [`adr/`](adr/)); los ADR se revisan en PR como el código.
- El equipo presenta en las demos, no el líder.
- *Pairing* en piezas críticas (outbox, consumidor paralelo, seguridad).
- Guardia rotativa **acompañada** al inicio, con el [manual de guardia](operacion.md).
- Autonomía con barandas automáticas: lo que el pipeline verifica (pruebas de arquitectura, contrato, seguridad) no depende de que alguien lo recuerde.

## 4. Estándares
Pocos, acordados y automatizados. Se parte de un borrador del líder y se discuten en taller. Los comentarios de revisión se etiquetan **bloqueante / sugerencia / detalle**. Se revisan en las retros.

Estándares que este repositorio ya automatiza: compilación con avisos como errores, dependencias de la hexagonal, contrato OpenAPI, vulnerabilidades (NuGet y Trivy) y congelamiento en temporada pico.

## 5. Mentorías
Plan individual por persona: *pairing*, katas de Kafka (rebalanceos, idempotencia, replay), análisis de incidentes y consultas abiertas. El senior también mentorea: enseñar consolida lo que sabe y reparte el conocimiento.

## 6. Desacuerdos técnicos
Criterios antes que opiniones. Datos antes que jerarquía: una prueba de concepto corta resuelve más que una reunión larga. Las decisiones reversibles se toman rápido; las irreversibles, con análisis. Tiempo límite y decisión documentada en un ADR. *Discrepar y comprometerse.*

Ejemplo de este proyecto: la pregunta "¿el procesador aguanta el pico?" no se discutió; se midió (C1: ~90 ev/s por instancia), se cambió el diseño (ADR-0011) y se volvió a medir (C2: ~343 ev/s).

## 7. Relación con producto
- Opciones con consecuencias: "antes del pico con riesgo, o en enero sin riesgo".
- La deuda técnica se presenta como riesgo de negocio.
- 20 % de la capacidad reservada para mejoras.
- Los SLO son un contrato compartido con producto, no una métrica de ingeniería.

## 8. Juniors
Seguridad psicológica. Preguntas antes que respuestas. Retroalimentación SCI (situación, comportamiento, impacto). Críticas en privado, reconocimiento en público. Postmortems sin culpables.

## Indicadores de que el equipo no involuciona
- Métricas DORA estables o mejorando.
- Ningún componente con un solo conocedor.
- ADR escritos por distintas personas.
- Tiempo hasta el primer despliegue de una persona nueva < 2 semanas.
- Satisfacción en las retros.
