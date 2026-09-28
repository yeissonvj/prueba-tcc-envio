# Visión a 18 meses (octubre 2026 – marzo 2028)

| Etapa | Periodo | Hito |
|---|---|---|
| Meses 1–3 | oct–dic 2026 | Modo sombra durante el pico: la plataforma recibe una copia del tráfico real y se compara con el sistema actual |
| Meses 4–6 | ene–mar 2027 | Migración gradual, un sistema emisor a la vez (TMS primero) |
| Meses 7–12 | abr–sep 2027 | Todos los emisores + agente de novedades en producción |
| **Meses 13–15** | **oct–dic 2027** | **Primer pico real sobre la nueva plataforma** |
| Meses 16–18 | ene–mar 2028 | La plataforma es el estándar de TCC para eventos |

## La escena de éxito · Cyber Lunes 2027
El volumen se multiplica por cuatro. Los consumidores escalan solos por lag. El proveedor de SMS cae 40 minutos y nadie fuera del equipo lo nota: los mensajes esperan en los tópicos de reintento y salen por correo. El cliente ve "en reparto" segundos después del escaneo. El agente de novedades resuelve una dirección por WhatsApp y el paquete se entrega esa misma tarde. La reconciliación del día confirma cero eventos perdidos. **El líder técnico estaba de vacaciones.**

## Metas medibles
- 0 eventos perdidos (reconciliación diaria).
- "Estado visible" p95 < 5 s en pico.
- 0 incidentes graves que afecten a clientes en pico.
- Varios despliegues por semana fuera del congelamiento; recuperación < 30 min.
- 0 componentes con un solo conocedor; primer despliegue de una persona nueva < 2 semanas.

## Qué NO es el éxito
Decenas de microservicios o la tecnología más moderna. El éxito es **un sistema aburrido en temporada pico**.

## Siguientes pasos técnicos (desde lo que ya existe)
1. **Versión Java/Spring Boot** sobre los mismos contratos, tópicos y migraciones ([ADR-0012](adr/0012-nombres-en-espanol-y-diseno-portable.md)).
2. Certificar capacidad en infraestructura dedicada ([pruebas](pruebas.md#qué-haría-falta-para-certificar-5000-evs)).
3. Proyector de estado en Redis + WebSocket para el portal (lado de lectura a escala).
4. Adaptadores de integración (TMS, gestión documental, internacional) como grupos de consumo propios.
5. Inbox separado del historial particionado cuando el volumen lo exija ([ADR-0003](adr/0003-inbox-en-historial-sin-particionar.md)).
6. Agente de novedades ([ia.md](ia.md)).
