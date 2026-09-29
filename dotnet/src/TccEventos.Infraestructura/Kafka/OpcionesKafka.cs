using Confluent.Kafka;

namespace TccEventos.Infraestructura.Kafka;

/// <summary>
/// Configuración de Kafka (sección "Kafka"). También es el único lugar donde se arma la conexión
/// de cualquier cliente de Kafka del sistema.
/// </summary>
public class OpcionesKafka
{
    /// <summary>Lista de brokers separados por coma (bootstrap servers).</summary>
    public string Servidores { get; init; } = "";

    /// <summary>Tópico donde la API publica los eventos recibidos.</summary>
    public string TopicoEventosRecibidos { get; init; } = "guias.eventos.recibidos";

    /// <summary>Tópico donde el relay publica los cambios de estado confirmados.</summary>
    public string TopicoEstadosCambiados { get; init; } = "guias.estados.cambiados";

    /// <summary>Tópico donde el procesador deja los mensajes que no pudo procesar.</summary>
    public string TopicoEventosDlq { get; init; } = "guias.eventos.dlq";

    /// <summary>Tiempo máximo para que Kafka confirme un mensaje (delivery.timeout.ms).</summary>
    public int TiempoMaximoEntregaMs { get; init; } = 5000;

    // Debe coincidir con min.insync.replicas del tópico: por debajo, acks=all rechaza escrituras.
    /// <summary>Réplicas sincronizadas mínimas para considerar que Kafka puede escribir (lo usa la sonda de salud).</summary>
    public int ReplicasMinimasSincronizadas { get; init; } = 2;

    // Circuito: se abre si en la ventana falla al menos la mitad de un mínimo de envíos.
    /// <summary>Envíos mínimos en la ventana antes de que el circuito pueda abrirse.</summary>
    public int CircuitoMinimoEnvios { get; init; } = 10;

    /// <summary>Tamaño de la ventana en la que se mide la tasa de fallas.</summary>
    public int CircuitoVentanaSegundos { get; init; } = 10;

    /// <summary>Cuánto tiempo permanece abierto el circuito antes de volver a probar.</summary>
    public int CircuitoSegundosAbierto { get; init; } = 15;

    // ---- Conexión segura (Kafka administrado, p. ej. Aiven). Sin ProtocoloSeguridad = PLAINTEXT (entorno local) ----

    /// <summary>SSL (certificado de cliente) o SASL_SSL (usuario y contraseña SCRAM sobre TLS).</summary>
    public string? ProtocoloSeguridad { get; init; }
    /// <summary>Ruta al certificado de la autoridad que firmó los brokers (ca.pem).</summary>
    public string? CertificadoCa { get; init; }
    /// <summary>Solo con SSL: certificado del cliente (service.cert).</summary>
    public string? CertificadoCliente { get; init; }
    /// <summary>Solo con SSL: llave del cliente (service.key).</summary>
    public string? LlaveCliente { get; init; }
    /// <summary>Solo con SASL_SSL: SCRAM-SHA-256 o SCRAM-SHA-512. La contraseña llega por variable de entorno.</summary>
    public string? MecanismoSasl { get; init; }
    /// <summary>Solo con SASL_SSL: usuario.</summary>
    public string? UsuarioSasl { get; init; }
    /// <summary>Solo con SASL_SSL: contraseña (nunca en el repositorio).</summary>
    public string? ContrasenaSasl { get; init; }

    /// <summary>
    /// Único lugar donde se configura cómo se conecta CUALQUIER cliente de Kafka del sistema
    /// (productor, consumidores, sonda de salud): nadie se conecta con otras reglas por accidente.
    /// </summary>
    /// <typeparam name="T">Tipo de configuración (productor, consumidor o administración).</typeparam>
    /// <param name="configuracion">Configuración a completar.</param>
    /// <returns>La misma configuración con servidores y seguridad aplicados.</returns>
    public T ConfigurarConexion<T>(T configuracion) where T : ClientConfig
    {
        configuracion.BootstrapServers = Servidores;
        if (string.IsNullOrWhiteSpace(ProtocoloSeguridad))
            return configuracion;

        configuracion.SecurityProtocol = Enum.Parse<SecurityProtocol>(ProtocoloSeguridad.Replace("_", ""), ignoreCase: true);
        configuracion.SslCaLocation = Valor(CertificadoCa);
        configuracion.SslCertificateLocation = Valor(CertificadoCliente);
        configuracion.SslKeyLocation = Valor(LlaveCliente);

        if (!string.IsNullOrWhiteSpace(MecanismoSasl))
        {
            configuracion.SaslMechanism = Enum.Parse<SaslMechanism>(MecanismoSasl.Replace("-", ""), ignoreCase: true);
            configuracion.SaslUsername = UsuarioSasl;
            configuracion.SaslPassword = ContrasenaSasl;
        }

        return configuracion;
    }

    // Una variable de entorno vacía (p. ej. sin certificado de cliente en SASL) cuenta como "no configurado".
    /// <summary>Convierte un texto vacío en <see langword="null"/>.</summary>
    /// <param name="texto">Valor configurado.</param>
    /// <returns>El texto, o <see langword="null"/> si está vacío.</returns>
    private static string? Valor(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}
