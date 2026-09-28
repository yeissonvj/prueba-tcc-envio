# ADR-0004 · Redis como filtro de duplicados tolerante a fallas

**Estado:** aceptada

## Contexto
Los emisores reintentan (red móvil de mensajeros, timeouts). Descartar duplicados en la API ahorra trabajo aguas abajo, pero esa verificación no puede convertirse en un punto único de falla de la ingesta.

## Decisión
- `FiltroDuplicadosRedis` (adaptador) envuelto en `FiltroDuplicadosTolerante` (**decorador**, en la capa de aplicación): si Redis falla, se considera "no visto" y se sigue.
- El filtro se marca **después** de publicar: si la publicación falla, el reintento del emisor no se descarta.
- `BacklogPolicy.FailFast` y `asyncTimeout=250 ms`: con Redis caído no se espera la reconexión.

## Consecuencias
- Ante la duda se publica de nuevo y el **inbox** del procesador deduplica: el peor caso es trabajo extra, nunca pérdida.
- Medido: sin `FailFast`, cada petición con Redis caído esperaba ~6 s adicionales.
- Hallazgo: `localhost` resolvía a IPv6 y Redis solo escucha en `127.0.0.1`; el filtro fallaba en silencio (el decorador hacía su trabajo). Se usa `127.0.0.1`.

## Alternativas descartadas
- **Filtro obligatorio (fallar si Redis falla):** convierte una optimización en dependencia crítica.
