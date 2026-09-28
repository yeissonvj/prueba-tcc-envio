#!/usr/bin/env bash
# Crea en Aiven los 5 tópicos que admite el plan gratuito (2 particiones cada uno), con la misma
# autenticación que usan las aplicaciones. Lee infra/aiven/.env y infra/aiven/certificados/.
# Alternativa sin script: crearlos en la consola de Aiven (pestaña Topics) con los mismos nombres.
set -euo pipefail
export MSYS_NO_PATHCONV=1

DIRECTORIO="$(cd "$(dirname "$0")" && pwd)"
set -a; source "$DIRECTORIO/.env"; set +a
CERTIFICADOS="$DIRECTORIO/certificados"
command -v cygpath >/dev/null && CERTIFICADOS=$(cygpath -m "$CERTIFICADOS")

if [ "${AIVEN_KAFKA_PROTOCOLO:-SSL}" = "SSL" ]; then
  PROPIEDADES='security.protocol=SSL
ssl.truststore.type=PEM
ssl.truststore.location=/certificados/ca.pem
ssl.keystore.type=PEM
ssl.keystore.location=/tmp/cliente.pem'
else
  PROPIEDADES="security.protocol=SASL_SSL
ssl.truststore.type=PEM
ssl.truststore.location=/certificados/ca.pem
sasl.mechanism=${AIVEN_KAFKA_MECANISMO:-SCRAM-SHA-256}
sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required username=\"${AIVEN_KAFKA_USUARIO}\" password=\"${AIVEN_KAFKA_CONTRASENA}\";"
fi

docker run --rm -i -v "$CERTIFICADOS:/certificados:ro" -e PROPIEDADES="$PROPIEDADES" -e SERVIDORES="$AIVEN_KAFKA_SERVIDORES" \
  --entrypoint /bin/bash apache/kafka:4.1.2 -c '
    set -e
    printf "%s\n" "$PROPIEDADES" > /tmp/cliente.properties
    [ -f /certificados/service.key ] && cat /certificados/service.key /certificados/service.cert > /tmp/cliente.pem
    for topico in guias.eventos.recibidos guias.estados.cambiados guias.eventos.dlq notificaciones.reintento.1m notificaciones.dlq; do
      /opt/kafka/bin/kafka-topics.sh --bootstrap-server "$SERVIDORES" --command-config /tmp/cliente.properties \
        --create --if-not-exists --topic "$topico" --partitions 2
    done
    echo "Tópicos en Aiven:"
    /opt/kafka/bin/kafka-topics.sh --bootstrap-server "$SERVIDORES" --command-config /tmp/cliente.properties --list'
