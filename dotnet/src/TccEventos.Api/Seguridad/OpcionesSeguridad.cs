namespace TccEventos.Api.Seguridad;

/// <summary>Configuración de seguridad (sección "Seguridad").</summary>
public class OpcionesSeguridad
{
    /// <summary>URL del emisor de tokens (realm de Keycloak u otro IdP OIDC). De ahí se descargan las llaves públicas.</summary>
    public string Emisor { get; init; } = "";

    /// <summary>
    /// Opcional: URL interna del IdP para descargar metadatos y llaves cuando la pública (Emisor, la que va
    /// en el claim iss) no es alcanzable desde la red de la API (p. ej. dentro de Docker o del clúster).
    /// </summary>
    public string? DireccionInterna { get; init; }

    /// <summary>Audiencia esperada en el token; rechaza tokens emitidos para otras APIs.</summary>
    public string Audiencia { get; init; } = "api-ingesta";

    /// <summary>Solo Desarrollo puede desactivarlo (Keycloak local sin TLS).</summary>
    public bool RequiereHttps { get; init; } = true;

    /// <summary>Qué valores de "origen" puede reportar cada cliente (claim azp). Cliente ausente = no puede escribir.</summary>
    public Dictionary<string, string[]> OrigenesPorCliente { get; init; } = [];

    /// <summary>Peticiones por segundo que recupera el cubo de cada cliente.</summary>
    public int PeticionesPorSegundoPorCliente { get; init; } = 1000;

    /// <summary>Tamaño máximo del cubo de cada cliente (ráfaga permitida).</summary>
    public int RafagaPorCliente { get; init; } = 2000;
}
