using TccEventos.Contratos.V1;

namespace TccEventos.Notificador.Enrutamiento;

/// <summary>
/// Mueve una notificación a su siguiente etapa (reintento o DLQ).
/// </summary>
/// <remarks>
/// Solo termina con éxito si el mensaje quedó guardado: de lo contrario el offset del mensaje actual no puede avanzar.
/// </remarks>
public interface IEnrutadorNotificaciones
{
    /// <summary>Publica en la etapa de reintento correspondiente a pendiente.Intento (1 = primera etapa).</summary>
    /// <param name="pendiente">Notificación a reintentar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando quedó programado.</returns>
    Task ProgramarReintentoAsync(NotificacionPendienteV1 pendiente, CancellationToken ct);

    /// <summary>Envía un mensaje a la DLQ de notificaciones.</summary>
    /// <param name="clave">Clave del mensaje (el número de guía).</param>
    /// <param name="contenido">Contenido a guardar.</param>
    /// <param name="motivo">Por qué se rechaza.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando quedó guardado.</returns>
    Task EnviarADlqAsync(string clave, string? contenido, string motivo, CancellationToken ct);
}
