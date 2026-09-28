-- Procesador de estado: estado actual, historial (que es también el inbox) y bandeja de salida (outbox).
-- Los estados se guardan con el texto del contrato (EN_REPARTO), igual para .NET y Java.

-- Estado actual: una fila por guía. "version" sube con cada cambio aplicado (concurrencia optimista
-- y número de orden del cambio para los consumidores).
CREATE TABLE guias (
    numero_guia       VARCHAR(30)  PRIMARY KEY,
    estado_actual     VARCHAR(30)  NOT NULL,
    ultimo_evento_en  TIMESTAMPTZ  NOT NULL,
    version           BIGINT       NOT NULL,
    actualizado_en    TIMESTAMPTZ  NOT NULL DEFAULT now()
);

-- Historial + inbox: id_evento es la llave primaria, así un evento repetido nunca se procesa dos veces.
-- Sin particionar a propósito: en una tabla particionada la PK debe incluir la columna de partición
-- y id_evento dejaría de ser único globalmente (ver ADR).
CREATE TABLE historial_eventos (
    id_evento     UUID          PRIMARY KEY,
    numero_guia   VARCHAR(30)   NOT NULL,
    estado        VARCHAR(30)   NOT NULL,
    ocurrido_en   TIMESTAMPTZ   NOT NULL,
    origen        VARCHAR(30)   NOT NULL,
    novedad       VARCHAR(500),
    resultado     VARCHAR(30)   NOT NULL,   -- APLICADO | TARDIO | TRANSICION_INVALIDA
    procesado_en  TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE INDEX ix_historial_eventos_guia ON historial_eventos (numero_guia, ocurrido_en);

-- Outbox: se escribe en la misma transacción que el estado; un relay lo publica en
-- guias.estados.cambiados y borra lo publicado.
CREATE TABLE bandeja_salida (
    id           BIGSERIAL     PRIMARY KEY,
    numero_guia  VARCHAR(30)   NOT NULL,   -- clave de partición en Kafka
    tipo         VARCHAR(50)   NOT NULL,
    carga        JSONB         NOT NULL,
    creado_en    TIMESTAMPTZ   NOT NULL DEFAULT now()
);
