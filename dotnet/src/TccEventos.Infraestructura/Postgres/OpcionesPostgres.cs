namespace TccEventos.Infraestructura.Postgres;

/// <summary>Configuración de PostgreSQL (sección "Postgres").</summary>
public class OpcionesPostgres
{
    /// <summary>Cadena de conexión sin contraseña (servidor, base, usuario, tiempos de espera).</summary>
    public string Conexion { get; init; } = "";

    /// <summary>Contraseña; llega por variable de entorno y nunca va en el repositorio.</summary>
    public string? Contrasena { get; init; }
}
