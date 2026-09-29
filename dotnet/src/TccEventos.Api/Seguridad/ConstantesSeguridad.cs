namespace TccEventos.Api.Seguridad;

/// <summary>Nombres de los claims del token (sin el mapeo automático de .NET: los mismos que ve Spring).</summary>
public static class Reclamos
{
    /// <summary>Cliente que pidió el token (authorized party).</summary>
    public const string Cliente = "azp";

    /// <summary>Alcances concedidos, separados por espacios.</summary>
    public const string Alcances = "scope";
}

/// <summary>Alcances OAuth2 que entiende la API.</summary>
public static class Alcances
{
    /// <summary>Permite enviar eventos (sistemas emisores).</summary>
    public const string EscribirEventos = "eventos:escribir";

    /// <summary>Permite consultar guías (portal de rastreo).</summary>
    public const string LeerGuias = "guias:leer";
}

/// <summary>Nombres de las políticas de autorización y de límite de peticiones.</summary>
public static class Politicas
{
    /// <summary>Exige el alcance eventos:escribir.</summary>
    public const string EscribirEventos = "escribir-eventos";

    /// <summary>Exige el alcance guias:leer.</summary>
    public const string LeerGuias = "leer-guias";

    /// <summary>Cubo de fichas por cliente (429 al superarlo).</summary>
    public const string LimitePorCliente = "limite-por-cliente";
}
