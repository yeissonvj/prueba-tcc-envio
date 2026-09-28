#!/bin/sh
# Crea los tópicos de la plataforma. Es idempotente: puede ejecutarse varias veces.
set -e

BOOTSTRAP="kafka-1:19092"

crear() {
  /opt/kafka/bin/kafka-topics.sh --bootstrap-server "$BOOTSTRAP" \
    --create --if-not-exists \
    --topic "$1" --partitions "$2" --replication-factor 3 \
    --config retention.ms="$3" \
    --config min.insync.replicas=2
}

# Retenciones en milisegundos
SIETE_DIAS=604800000
UN_DIA=86400000
TREINTA_DIAS=2592000000

crear guias.eventos.recibidos      24 $SIETE_DIAS
crear guias.estados.cambiados      24 $SIETE_DIAS
crear notificaciones.reintento.1m   6 $UN_DIA
crear notificaciones.reintento.10m  6 $UN_DIA
crear notificaciones.reintento.1h   6 $UN_DIA
crear guias.eventos.dlq              3 $TREINTA_DIAS
crear notificaciones.dlq            3 $TREINTA_DIAS

echo "Tópicos disponibles:"
/opt/kafka/bin/kafka-topics.sh --bootstrap-server "$BOOTSTRAP" --list