using System.Net.Sockets;
using Npgsql;
using TccEventos.Aplicacion.Puertos;

namespace TccEventos.Infraestructura.Resiliencia;

/// Transitorio = se arregla solo si se espera (base caída, red, broker, proveedor, conflicto de concurrencia).
/// Cada consumidor decide qué hacer con eso: el procesador espera (bloqueante), el notificador reprograma.
public static class ClasificadorErrores
{
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
