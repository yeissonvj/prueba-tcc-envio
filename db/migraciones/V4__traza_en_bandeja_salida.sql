-- El relay publica la bandeja de salida después y desde otro hilo: sin guardar el contexto de traza
-- (W3C traceparent) junto al mensaje, la traza del evento se "cortaría" en la base de datos.
ALTER TABLE bandeja_salida ADD COLUMN contexto_traza VARCHAR(55);
