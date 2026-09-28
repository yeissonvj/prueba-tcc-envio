using Confluent.Kafka;

namespace TccEventos.Infraestructura.Kafka;

public class OpcionesKafka
{
    public string Servidores { get; init; } = "";
    public string TopicoEventosRecibidos { get; init; } = "guias.eventos.recibidos";
    public string TopicoEstadosCambiados { get; init; } = "guias.estados.cambiados";
    public string TopicoEventosDlq { get; init; } = "guias.eventos.dlq";
    public int TiempoMaximoEntregaMs { get; init; } = 5000;

    // Debe coincidir con min.insync.replicas del tópico: por debajo, acks=all rechaza escrituras.
    public int ReplicasMinimasSincronizadas { get; init; } = 2;

    // Circuito: se abre si en la ventana falla al menos la mitad de un mínimo de envíos.
    public int CircuitoMinimoEnvios { get; init; } = 10;
    public int CircuitoVentanaSegundos { get; init; } = 10;
    public int CircuitoSegundosAbierto { get; init; } = 15;

    // ---- Conexión segura (Kafka administrado, p. ej. Aiven). Sin ProtocoloSeguridad = PLAINTEXT (entorno local) ----

    /// SSL (certificado de cliente) o SASL_SSL (usuario y contraseña SCRAM sobre TLS).
    public string? ProtocoloSeguridad { get; init; }
    /// Ruta al certificado de la autoridad que firmó los brokers (ca.pem).
    public string? CertificadoCa { get; init; }
    /// Solo con SSL: certificado y llave del cliente (service.cert, service.key).
    public string? CertificadoCliente { get; init; }
    public string? LlaveCliente { get; init; }
    /// Solo con SASL_SSL: SCRAM-SHA-256 o SCRAM-SHA-512. La contraseña llega por variable de entorno.
    public string? MecanismoSasl { get; init; }
    public string? UsuarioSasl { get; init; }
    public string? ContrasenaSasl { get; init; }

    /// Único lugar donde se configura cómo se conecta CUALQUIER cliente de Kafka del sistema
    /// (productor, consumidores, sonda de salud): nadie se conecta con otras reglas por accidente.
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
    private static string? Valor(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}
