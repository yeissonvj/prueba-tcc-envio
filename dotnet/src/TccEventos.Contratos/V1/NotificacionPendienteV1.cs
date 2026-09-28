namespace TccEventos.Contratos.V1;

public static class CanalesV1
{
    public const string Sms = "SMS";
    public const string Correo = "CORREO";
}

/// Mensaje de los tópicos notificaciones.reintento.* y notificaciones.dlq.
/// Intento cuenta los reintentos ya hechos por ese canal (0 = primer envío).
/// RecibidoEn: cuándo llegó el cambio a guias.estados.cambiados (mide la latencia real, reintentos incluidos).
/// Opcional para ser compatible con mensajes anteriores.
public record NotificacionPendienteV1(EstadoGuiaCambiadoV1 Cambio, string Canal, int Intento, DateTimeOffset? RecibidoEn = null);
