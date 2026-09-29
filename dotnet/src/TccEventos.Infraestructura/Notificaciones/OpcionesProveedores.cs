namespace TccEventos.Infraestructura.Notificaciones;

/// <summary>Comportamiento de un proveedor simulado, para probar fallas sin un proveedor real.</summary>
public class OpcionesProveedorSimulado
{
    /// <summary>Simula una caída total del proveedor (HTTP 503).</summary>
    public bool Caido { get; init; }

    /// <summary>Fracción de envíos que fallan al azar (0 a 1), como timeouts intermitentes.</summary>
    public double TasaFallas { get; init; }

    /// <summary>Demora simulada de cada envío, en milisegundos.</summary>
    public int LatenciaMs { get; init; } = 30;
}

/// <summary>Configuración de los proveedores de notificación (sección "Proveedores").</summary>
public class OpcionesProveedores
{
    /// <summary>Proveedor de SMS.</summary>
    public OpcionesProveedorSimulado Sms { get; init; } = new();

    /// <summary>Proveedor de correo.</summary>
    public OpcionesProveedorSimulado Correo { get; init; } = new();

    // Circuito por proveedor: se abre si en la ventana falla al menos la mitad de un mínimo de envíos.
    /// <summary>Envíos mínimos en la ventana antes de que el circuito pueda abrirse.</summary>
    public int CircuitoMinimoEnvios { get; init; } = 10;

    /// <summary>Tamaño de la ventana en la que se mide la tasa de fallas.</summary>
    public int CircuitoVentanaSegundos { get; init; } = 30;

    /// <summary>Cuánto tiempo permanece abierto el circuito antes de volver a probar.</summary>
    public int CircuitoSegundosAbierto { get; init; } = 30;
}
