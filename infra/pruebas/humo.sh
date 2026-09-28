#!/usr/bin/env bash
# Prueba de humo de punta a punta contra un entorno ya levantado (local con el perfil "aplicaciones",
# o staging). Verifica el contrato básico y que un evento aceptado se vuelve visible a tiempo.
#   API=http://localhost:8090 ./infra/pruebas/humo.sh
set -euo pipefail

API="${API:-http://localhost:8090}"
TOKEN_URL="${TOKEN_URL:-http://localhost:8081/realms/tcc/protocol/openid-connect/token}"
SECRETO_TMS="${SECRETO_TMS:-tms-secreto-solo-local}"
SECRETO_PORTAL="${SECRETO_PORTAL:-portal-secreto-solo-local}"
SEGUNDOS_MAXIMOS="${SEGUNDOS_MAXIMOS:-10}"   # SLO "estado visible" p95 < 5 s, con margen

paso()  { echo "▶ $*"; }
bien()  { echo "  ✓ $*"; }
falla() { echo "  ✗ $*" >&2; exit 1; }

token() {
  curl -fsS -X POST "$TOKEN_URL" -d grant_type=client_credentials -d client_id="$1" -d client_secret="$2" \
    | sed -E 's/.*"access_token":"([^"]+)".*/\1/'
}
uuid() {
  local h; h=$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')
  echo "${h:0:8}-${h:8:4}-4${h:13:3}-a${h:17:3}-${h:20:12}"
}
estado_http() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
evento() {
  printf '{"idEvento":"%s","numeroGuia":"%s","estado":"%s","ocurridoEn":"%s","origen":"TMS"}' "$1" "$2" "$3" "$(date -u -d '-1 minute' +%Y-%m-%dT%H:%M:%SZ)"
}

paso "La API está lista"
for intento in $(seq 1 60); do
  [ "$(estado_http "$API/salud/lista")" = 200 ] && break
  [ "$intento" = 60 ] && falla "La API no quedó lista en 2 minutos"
  sleep 2
done
bien "/salud/lista → 200"

TMS=$(token tms "$SECRETO_TMS")
PORTAL=$(token portal-consulta "$SECRETO_PORTAL")
GUIA="HUMO$(date +%s)"
ID=$(uuid)

paso "Contrato de ingesta"
[ "$(estado_http -X POST "$API/api/v1/eventos-guia" -H 'Content-Type: application/json' -d '{}')" = 401 ] || falla "sin token no respondió 401"
bien "sin token → 401"
codigo=$(estado_http -X POST "$API/api/v1/eventos-guia" -H "Authorization: Bearer $TMS" -H 'Content-Type: application/json' -d "$(evento "$ID" "$GUIA" CREADA)")
[ "$codigo" = 202 ] || falla "evento válido respondió $codigo (esperado 202)"
bien "evento válido → 202"
T0=$(date +%s%N)
codigo=$(estado_http -X POST "$API/api/v1/eventos-guia" -H "Authorization: Bearer $TMS" -H 'Content-Type: application/json' -d "$(evento "$ID" "$GUIA" CREADA)")
[ "$codigo" = 200 ] || falla "duplicado respondió $codigo (esperado 200)"
bien "mismo idEvento → 200 (duplicado sin efecto)"
codigo=$(estado_http -X POST "$API/api/v1/eventos-guia" -H "Authorization: Bearer $TMS" -H 'Content-Type: application/json' -d "$(evento "$(uuid)" "X-1" PERDIDA)")
[ "$codigo" = 400 ] || falla "evento inválido respondió $codigo (esperado 400)"
bien "evento inválido → 400"

paso "El estado es visible por la consulta en menos de ${SEGUNDOS_MAXIMOS} s"
limite=$(( $(date +%s) + SEGUNDOS_MAXIMOS ))
until curl -fsS "$API/api/v1/guias/$GUIA" -H "Authorization: Bearer $PORTAL" 2>/dev/null | grep -q '"estadoActual":"CREADA"'; do
  [ "$(date +%s)" -ge "$limite" ] && falla "la guía $GUIA no apareció en ${SEGUNDOS_MAXIMOS} s"
  sleep 0.2
done
bien "GET /api/v1/guias/$GUIA → CREADA en $(( ($(date +%s%N) - T0) / 1000000 )) ms"

echo "✓ Prueba de humo superada"
