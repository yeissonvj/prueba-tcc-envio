namespace TccEventos.Infraestructura.Redis;

public class OpcionesRedis
{
    public string Conexion { get; init; } = "";
    public string? Contrasena { get; init; }
    public string PrefijoClave { get; init; } = "tcc:eventos:recibidos:";
    public TimeSpan Vigencia { get; init; } = TimeSpan.FromHours(72);
}
