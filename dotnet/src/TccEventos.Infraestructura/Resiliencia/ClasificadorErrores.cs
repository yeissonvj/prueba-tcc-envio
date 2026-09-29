using System.Net.Sockets;
using Npgsql;
using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Infraestructura.Resiliencia;

/// <summary>
/// Clasifica los errores en transitorios (se arreglan solos si se espera: base caída, red, broker,
/// proveedor, conflicto de concurrencia) o no transitorios (por ejemplo, un bug).
/// </summary>
/// <remarks>
/// Cada consumidor decide qué hacer con eso: el procesador espera (bloqueante), el notificador reprograma.
/// </remarks>
public static class ClasificadorErrores
{
    /// <summary>Indica si el error es transitorio; revisa también las excepciones internas.</summary>
    /// <param name="ex">Error a clasificar.</param>
    /// <returns><see langword="true"/> si conviene reintentar.</returns>
    public static bool EsTransitorio(Exception ex) => ex switch
    {
        ConflictoConcurrenciaException => true,
        PublicacionFallidaException => true,
        ProveedorNoDisponibleException => true,
        TimeoutException => true,
        SocketException => true,
        NpgsqlException npgsql => npgsql.IsTransient,
        _ when ex.InnerException is not null => EsTransitorio(ex.InnerException),
        _ => false
    };
}
