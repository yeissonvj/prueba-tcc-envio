using Confluent.Kafka;
using TccEventos.Infraestructura.Kafka;

namespace TccEventos.Procesador.PruebasIntegracion;

public class OpcionesKafkaPruebas
{
    [Fact]
    public void Sin_protocolo_de_seguridad_la_conexion_es_plaintext()
    {
        var configuracion = new OpcionesKafka { Servidores = "kafka:9092" }.ConfigurarConexion(new ProducerConfig());

        Assert.Equal("kafka:9092", configuracion.BootstrapServers);
        Assert.Null(configuracion.SecurityProtocol);
    }

    [Fact]
    public void Con_certificado_de_cliente_usa_ssl_con_los_tres_archivos()
    {
        var configuracion = new OpcionesKafka
        {
            Servidores = "servicio.aivencloud.com:12345",
            ProtocoloSeguridad = "SSL",
            CertificadoCa = "/certificados/ca.pem",
            CertificadoCliente = "/certificados/service.cert",
            LlaveCliente = "/certificados/service.key"
        }.ConfigurarConexion(new ConsumerConfig());

        Assert.Equal(SecurityProtocol.Ssl, configuracion.SecurityProtocol);
        Assert.Equal(("/certificados/ca.pem", "/certificados/service.cert", "/certificados/service.key"),
            (configuracion.SslCaLocation, configuracion.SslCertificateLocation, configuracion.SslKeyLocation));
        Assert.Null(configuracion.SaslMechanism);
    }

    [Fact]
    public void Con_sasl_scram_usa_tls_usuario_y_contrasena()
    {
        var configuracion = new OpcionesKafka
        {
            Servidores = "servicio.aivencloud.com:12346",
            ProtocoloSeguridad = "SASL_SSL",
            CertificadoCa = "/certificados/ca.pem",
            MecanismoSasl = "SCRAM-SHA-256",
            UsuarioSasl = "avnadmin",
            ContrasenaSasl = "no-es-real"
        }.ConfigurarConexion(new AdminClientConfig());

        Assert.Equal(SecurityProtocol.SaslSsl, configuracion.SecurityProtocol);
        Assert.Equal(SaslMechanism.ScramSha256, configuracion.SaslMechanism);
        Assert.Equal(("avnadmin", "no-es-real"), (configuracion.SaslUsername, configuracion.SaslPassword));
    }
}
