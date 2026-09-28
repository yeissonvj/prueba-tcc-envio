using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Infraestructura.Notificaciones;

/// Proveedor de SMS o correo simulado. Se comporta como uno real en lo que importa para las pruebas:
/// puede estar caído, fallar al azar, rechazar destinos inválidos y deduplicar por clave de idempotencia.
public sealed class ProveedorSimulado(
    CanalNotificacion canal,
    OpcionesProveedorSimulado opciones,
    ILogger<ProveedorSimulado> logger) : IProveedorNotificacion
{
    // Los proveedores reales recuerdan las claves de idempotencia un tiempo; aquí, mientras vive el proceso.
    private readonly ConcurrentDictionary<string, bool> _clavesAceptadas = new();

    public CanalNotificacion Canal => canal;

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

    public static string Enmascarar(string destino)
    {
        var arroba = destino.IndexOf('@');
        if (arroba > 0)
            return $"{destino[0]}***{destino[arroba..]}";
        return destino.Length <= 6 ? "***" : $"{destino[..3]}****{destino[^3..]}";
    }
}
