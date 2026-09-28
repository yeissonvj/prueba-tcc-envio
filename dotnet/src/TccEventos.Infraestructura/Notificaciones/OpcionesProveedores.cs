namespace TccEventos.Infraestructura.Notificaciones;

public class OpcionesProveedorSimulado
{
    /// Simula una caída total del proveedor (HTTP 503).
    public bool Caido { get; init; }

    /// Fracción de envíos que fallan al azar (0 a 1), como timeouts intermitentes.
    public double TasaFallas { get; init; }

    public int LatenciaMs { get; init; } = 30;
}

public class OpcionesProveedores
{
    public OpcionesProveedorSimulado Sms { get; init; } = new();
    public OpcionesProveedorSimulado Correo { get; init; } = new();

    // Circuito por proveedor: se abre si en la ventana falla al menos la mitad de un mínimo de envíos.
    public int CircuitoMinimoEnvios { get; init; } = 10;
    public int CircuitoVentanaSegundos { get; init; } = 30;
    public int CircuitoSegundosAbierto { get; init; } = 30;
}
