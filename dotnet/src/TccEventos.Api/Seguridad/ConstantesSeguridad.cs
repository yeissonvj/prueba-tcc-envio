namespace TccEventos.Api.Seguridad;

/// Nombres de los claims del token (sin el mapeo automático de .NET: los mismos que ve Spring).
public static class Reclamos
{
    public const string Cliente = "azp";
    public const string Alcances = "scope";
}

public static class Alcances
{
    public const string EscribirEventos = "eventos:escribir";
    public const string LeerGuias = "guias:leer";
}

public static class Politicas
{
    public const string EscribirEventos = "escribir-eventos";
    public const string LeerGuias = "leer-guias";
    public const string LimitePorCliente = "limite-por-cliente";
}
