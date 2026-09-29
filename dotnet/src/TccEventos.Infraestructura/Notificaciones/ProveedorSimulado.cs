using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Notificaciones;

/// <summary>
/// Proveedor de SMS o correo simulado. Se comporta como uno real en lo que importa para las pruebas:
/// puede estar caído, fallar al azar, rechazar destinos inválidos y deduplicar por clave de idempotencia.
/// </summary>
/// <param name="canal">Canal que atiende.</param>
/// <param name="opciones">Comportamiento simulado (caído, tasa de fallas, latencia).</param>
/// <param name="logger">Registro de los envíos (con el destino enmascarado).</param>
public sealed class ProveedorSimulado(
    CanalNotificacion canal,
    OpcionesProveedorSimulado opciones,
    ILogger<ProveedorSimulado> logger) : IProveedorNotificacion
{
    // Los proveedores reales recuerdan las claves de idempotencia un tiempo; aquí, mientras vive el proceso.
    /// <summary>Claves de idempotencia ya aceptadas.</summary>
    private readonly ConcurrentDictionary<string, bool> _clavesAceptadas = new();

    /// <summary>Canal que atiende este proveedor.</summary>
    public CanalNotificacion Canal => canal;

    /// <summary>Simula el envío del mensaje.</summary>
    /// <param name="mensaje">Mensaje a enviar.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Una tarea que termina cuando el envío simulado se completó (o se ignoró por repetido).</returns>
    /// <exception cref="ProveedorNoDisponibleException">Proveedor caído o falla al azar.</exception>
    /// <exception cref="DestinoRechazadoException">El destino empieza por 000 (inválido).</exception>
    public async Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct)
    {
        await Task.Delay(opciones.LatenciaMs, ct);

        if (opciones.Caido)
            throw new ProveedorNoDisponibleException($"Proveedor de {canal} no disponible (HTTP 503 simulado).");
        if (opciones.TasaFallas > 0 && Random.Shared.NextDouble() < opciones.TasaFallas)
            throw new ProveedorNoDisponibleException($"Proveedor de {canal}: timeout simulado.");
        if (mensaje.Destino.StartsWith("000", StringComparison.Ordinal))
            throw new DestinoRechazadoException($"Proveedor de {canal}: destino inválido.");

        if (!_clavesAceptadas.TryAdd(mensaje.ClaveIdempotencia, true))
        {
            logger.LogInformation("{Canal}: clave {Clave} repetida, el proveedor la ignora", canal, mensaje.ClaveIdempotencia);
            return;
        }

        // Nunca el destino completo en los logs (Ley 1581).
        logger.LogInformation("{Canal} a {Destino} [{Clave}]: {Texto}",
            canal, Enmascarar(mensaje.Destino), mensaje.ClaveIdempotencia, mensaje.Texto);
    }

    /// <summary>Oculta la mayor parte del destino para no exponer datos personales en los logs (Ley 1581).</summary>
    /// <param name="destino">Teléfono o correo.</param>
    /// <returns>Por ejemplo <c>300****567</c> o <c>c***@correo.co</c>.</returns>
    public static string Enmascarar(string destino)
    {
        var arroba = destino.IndexOf('@');
        if (arroba > 0)
            return $"{destino[0]}***{destino[arroba..]}";
        return destino.Length <= 6 ? "***" : $"{destino[..3]}****{destino[^3..]}";
    }
}
