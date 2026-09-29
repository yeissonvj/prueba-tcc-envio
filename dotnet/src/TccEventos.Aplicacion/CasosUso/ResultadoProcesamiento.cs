namespace TccEventos.Aplicacion.CasosUso;

/// <summary>Qué pasó al procesar un evento en el procesador de estado.</summary>
public enum ResultadoProcesamiento
{
    /// <summary>El evento cambió el estado de la guía.</summary>
    Aplicado,

    /// <summary>El evento era anterior al último aplicado: quedó en el historial sin cambiar el estado.</summary>
    Tardio,

    /// <summary>El cambio no está permitido por la máquina de estados: quedó en el historial.</summary>
    TransicionInvalida,

    /// <summary>El evento ya se había procesado antes (inbox); no tuvo efecto.</summary>
    Duplicado
}
