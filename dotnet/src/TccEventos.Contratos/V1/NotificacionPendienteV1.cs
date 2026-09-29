namespace TccEventos.Contratos.V1;

/// <summary>Textos de los canales de notificación en el contrato V1.</summary>
public static class CanalesV1
{
    /// <summary>Mensaje de texto.</summary>
    public const string Sms = "SMS";

    /// <summary>Correo electrónico.</summary>
    public const string Correo = "CORREO";
}

/// <summary>
/// Mensaje de los tópicos notificaciones.reintento.* y notificaciones.dlq: una notificación que todavía
/// no se ha podido enviar.
/// </summary>
/// <param name="Cambio">Cambio de estado que se quiere notificar.</param>
/// <param name="Canal">Canal por el que se intenta (<see cref="CanalesV1"/>).</param>
/// <param name="Intento">Reintentos ya hechos por ese canal (0 = primer envío).</param>
/// <param name="RecibidoEn">
/// Cuándo llegó el cambio a guias.estados.cambiados (mide la latencia real, reintentos incluidos).
/// Opcional para ser compatible con mensajes anteriores.
/// </param>
public record NotificacionPendienteV1(EstadoGuiaCambiadoV1 Cambio, string Canal, int Intento, DateTimeOffset? RecibidoEn = null);
