# ADR-0008 · OAuth2 client credentials y autorización por origen

**Estado:** aceptada

## Contexto
La API recibe eventos de varios sistemas internos y sirve consultas al portal de rastreo. Un sistema comprometido o con un bug no debe poder escribir en nombre de otro ni saturar a los demás.

## Decisión
- **Autenticación:** OAuth2 *client credentials* (Keycloak en local; el IdP corporativo en producción). JWT **RS256** validado por emisor, audiencia `api-ingesta`, vigencia (5 min) y algoritmo (sin "alg confusion").
- **Autorización por alcance:** `eventos:escribir` para ingesta; `guias:leer` para consulta (menor privilegio).
- **Autorización sobre los datos:** cada cliente solo puede reportar sus propios valores de `origen` (TMS → `TMS`). Evita que un sistema se haga pasar por otro en el historial de la guía.
- **Límite por cliente** (token bucket, 429 + Retry-After) como segunda defensa; el límite global va en el API Gateway.
- URL pública del emisor (claim `iss`) separada de la URL interna para descargar las llaves (necesario dentro de Docker/Kubernetes).

## Consecuencias
- Verificado contra Keycloak real: sin token 401; suplantación 403; token solo de lectura 403; token alterado 401 ("signature is invalid"); TMS limitado (429) mientras Transporte sigue con 202.
- La primera petición autenticada descargaba la configuración OIDC (~2 s); se precarga al arrancar.
- En Spring el equivalente es `spring-boot-starter-oauth2-resource-server` con los mismos tokens.

## Alternativas descartadas
- **API keys:** sin expiración ni alcances, difíciles de rotar.
- **Solo seguridad en el Gateway:** sin defensa en profundidad y sin autorización sobre los datos.
