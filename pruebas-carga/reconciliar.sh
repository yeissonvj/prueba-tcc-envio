#!/usr/bin/env bash
# Reconciliación de la Fase 2.2: todo evento que recibió 202 debe existir en PostgreSQL.
# Espera a que el procesador termine (lag = 0) y compara. La diferencia aceptable es exactamente cero.
#   ./pruebas-carga/reconciliar.sh C1
set -euo pipefail
# En Git Bash (Windows), MSYS convierte "/opt/..." en una ruta de Windows antes de pasarlo a docker.
export MSYS_NO_PATHCONV=1

CORRIDA="${1:?Uso: reconciliar.sh <corrida>}"
DIRECTORIO="$(cd "$(dirname "$0")" && pwd)"
ARCHIVO_COMPOSE="$DIRECTORIO/../infra/docker-compose.yml"
command -v cygpath >/dev/null && ARCHIVO_COMPOSE=$(cygpath -m "$ARCHIVO_COMPOSE")   # solo en Git Bash
COMPOSE=(docker compose -f "$ARCHIVO_COMPOSE")
ESPERA_MAXIMA="${ESPERA_MAXIMA:-600}"

aceptados=$(sed -n 's/^aceptados=//p' "$DIRECTORIO/resultados/$CORRIDA.txt")
[ -n "$aceptados" ] || { echo "No hay resultados de k6 para la corrida $CORRIDA"; exit 1; }

contar() {
  "${COMPOSE[@]}" exec -T postgres psql -U tcc -d tcc_eventos -tAc \
    "SELECT count(*) FROM guias WHERE numero_guia LIKE '${CORRIDA}%'"
}
# Sin silenciar errores: si no se puede medir el lag, el script debe fallar, nunca reportar un 0 falso.
lag() {
  local salida
  salida=$("${COMPOSE[@]}" exec -T kafka-1 /opt/kafka/bin/kafka-consumer-groups.sh --bootstrap-server localhost:19092 \
    --describe --group procesador-estado)
  echo "$salida" | grep -q 'LOG-END-OFFSET' || { echo "No se pudo leer el lag del grupo procesador-estado" >&2; exit 2; }
  echo "$salida" | awk '$6 ~ /^[0-9]+$/ {s+=$6} END {print s+0}'
}

echo "Corrida $CORRIDA: k6 recibió $aceptados respuestas 202"
inicio=$(date +%s)
while :; do
  en_base=$(contar); pendiente=$(lag)
  transcurrido=$(( $(date +%s) - inicio ))
  echo "  +${transcurrido}s  en PostgreSQL: $en_base   lag del procesador: $pendiente"
  if [ "$pendiente" -eq 0 ] && [ "$en_base" -ge "$aceptados" ]; then break; fi
  [ "$transcurrido" -ge "$ESPERA_MAXIMA" ] && break
  sleep 5
done

diferencia=$(( aceptados - en_base ))
echo "Aceptados (202): $aceptados   En PostgreSQL: $en_base   Diferencia: $diferencia"
if [ "$diferencia" -eq 0 ]; then
  echo "✓ Reconciliación exacta: cero eventos perdidos"
else
  echo "✗ Reconciliación con diferencia: revisar DLQ, contingencia y logs" >&2
  exit 1
fi
