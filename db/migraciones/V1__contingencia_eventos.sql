-- Buffer durable de la API de ingesta: guarda los eventos que Kafka no confirmó.
-- El relay los publica en orden de llegada y los borra. Vacía en operación normal.
CREATE TABLE contingencia_eventos (
    id_evento    UUID         PRIMARY KEY,           -- idempotencia: un reintento del emisor no duplica
    numero_guia  VARCHAR(30)  NOT NULL,
    carga        JSONB        NOT NULL,              -- EventoGuiaV1 tal como se publicará
    recibido_en  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX ix_contingencia_eventos_recibido_en ON contingencia_eventos (recibido_en);
