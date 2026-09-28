namespace TccEventos.Api.Seguridad;

public class OpcionesSeguridad
{
    /// URL del emisor de tokens (realm de Keycloak u otro IdP OIDC). De ahí se descargan las llaves públicas.
    public string Emisor { get; init; } = "";

    /// Opcional: URL interna del IdP para descargar metadatos y llaves cuando la pública (Emisor, la que va
    /// en el claim iss) no es alcanzable desde la red de la API (p. ej. dentro de Docker o del clúster).
    public string? DireccionInterna { get; init; }
    public string Audiencia { get; init; } = "api-ingesta";

    /// Solo Desarrollo puede desactivarlo (Keycloak local sin TLS).
    public bool RequiereHttps { get; init; } = true;

    /// Qué valores de "origen" puede reportar cada cliente (claim azp). Cliente ausente = no puede escribir.
    public Dictionary<string, string[]> OrigenesPorCliente { get; init; } = [];

    public int PeticionesPorSegundoPorCliente { get; init; } = 1000;
    public int RafagaPorCliente { get; init; } = 2000;
}
