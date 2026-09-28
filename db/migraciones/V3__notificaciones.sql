-- Notificador: registro de notificaciones enviadas (idempotencia y descarte de obsoletas).
CREATE TABLE notificaciones_enviadas (
    clave        VARCHAR(80)  PRIMARY KEY,   -- numero_guia:version:canal
    numero_guia  VARCHAR(30)  NOT NULL,
    version      BIGINT       NOT NULL,
    canal        VARCHAR(10)  NOT NULL,
    enviada_en   TIMESTAMPTZ  NOT NULL DEFAULT now()
);

-- "¿Ya se notificó una versión más reciente de esta guía?" para no enviar mensajes viejos fuera de orden.
CREATE INDEX ix_notificaciones_enviadas_guia_version ON notificaciones_enviadas (numero_guia, version);
