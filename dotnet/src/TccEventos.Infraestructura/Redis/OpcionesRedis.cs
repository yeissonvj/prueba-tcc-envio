namespace TccEventos.Infraestructura.Redis;

/// <summary>Configuración de Redis (sección "Redis").</summary>
public class OpcionesRedis
{
    /// <summary>Cadena de conexión de StackExchange.Redis (servidor, tiempos de espera).</summary>
    public string Conexion { get; init; } = "";

    /// <summary>Contraseña; llega por variable de entorno.</summary>
    public string? Contrasena { get; init; }

    /// <summary>Prefijo de las claves del filtro de duplicados.</summary>
    public string PrefijoClave { get; init; } = "tcc:eventos:recibidos:";

    /// <summary>Cuánto tiempo se recuerda un evento recibido.</summary>
    public TimeSpan Vigencia { get; init; } = TimeSpan.FromHours(72);
}
