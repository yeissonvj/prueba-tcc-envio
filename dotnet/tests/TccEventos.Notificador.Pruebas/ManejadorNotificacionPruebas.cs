using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Contratos.V1;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Notificador.Enrutamiento;

namespace TccEventos.Notificador.Pruebas;

public class ManejadorNotificacionPruebas
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly OpcionesNotificador Opciones = new()
    {
        Reintentos =
        [
            new() { Topico = "r1", Espera = TimeSpan.FromMinutes(1) },
            new() { Topico = "r2", Espera = TimeSpan.FromMinutes(10) },
            new() { Topico = "r3", Espera = TimeSpan.FromHours(1) }
        ]
    };

    private readonly RegistroEnMemoria _registro = new();
    private readonly ProveedorProgramable _sms = new(CanalNotificacion.Sms);
    private readonly ProveedorProgramable _correo = new(CanalNotificacion.Correo);
    private readonly EnrutadorEnMemoria _enrutador = new();

    private ManejadorNotificacion Manejador(Contacto? contacto = null) => new(
        new NotificarCambioEstado(_registro, new DirectorioFijo(contacto ?? new Contacto("3001234567", "c@correo.co")), [_sms, _correo]),
        _enrutador, Opciones, NullLogger<ManejadorNotificacion>.Instance);

    private static EstadoGuiaCambiadoV1 Cambio(string estado = EstadosV1.EnReparto, long version = 3) =>
        new(Guid.NewGuid(), "TCC123", EstadosV1.EnBodegaDestino, estado, DateTimeOffset.UnixEpoch, "TRANSPORTE", null, version);

    private static MensajeKafka DeCambio(EstadoGuiaCambiadoV1 cambio) =>
        new("guias.estados.cambiados", 0, 1, cambio.NumeroGuia, JsonSerializer.Serialize(cambio, Json));

    private static MensajeKafka DeReintento(NotificacionPendienteV1 pendiente) =>
        new("r", 0, 1, pendiente.Cambio.NumeroGuia, JsonSerializer.Serialize(pendiente, Json));

    private static Func<Exception> Caido => () => new ProveedorNoDisponibleException("503");

    [Fact]
    public async Task Con_el_proveedor_disponible_notifica_por_sms_sin_reintentos()
    {
        await Manejador().ManejarCambioAsync(DeCambio(Cambio()), CancellationToken.None);

        Assert.Single(_sms.Enviados);
        Assert.Empty(_enrutador.Reintentos);
        Assert.Empty(_enrutador.Dlq);
    }

    [Fact]
    public async Task Un_estado_interno_no_genera_nada()
    {
        await Manejador().ManejarCambioAsync(DeCambio(Cambio(EstadosV1.EnTransito)), CancellationToken.None);

        Assert.Empty(_sms.Enviados);
        Assert.Empty(_enrutador.Reintentos);
    }

    [Fact]
    public async Task Con_el_sms_caido_programa_el_primer_reintento_sin_bloquear()
    {
        _sms.Falla = Caido;

        await Manejador().ManejarCambioAsync(DeCambio(Cambio()), CancellationToken.None);

        var reintento = Assert.Single(_enrutador.Reintentos);
        Assert.Equal((CanalesV1.Sms, 1), (reintento.Canal, reintento.Intento));
    }

    [Fact]
    public async Task Cada_reintento_fallido_avanza_a_la_siguiente_etapa()
    {
        _sms.Falla = Caido;

        await Manejador().ManejarReintentoAsync(DeReintento(new(Cambio(), CanalesV1.Sms, 1)), CancellationToken.None);

        Assert.Equal(2, Assert.Single(_enrutador.Reintentos).Intento);
    }

    [Fact]
    public async Task Agotados_los_reintentos_del_sms_se_usa_el_correo()
    {
        _sms.Falla = Caido;

        await Manejador().ManejarReintentoAsync(DeReintento(new(Cambio(), CanalesV1.Sms, 3)), CancellationToken.None);

        Assert.Single(_correo.Enviados);
        Assert.Empty(_enrutador.Dlq);
    }

    [Fact]
    public async Task Si_el_correo_tambien_falla_tiene_su_propia_escalera_y_al_final_va_a_la_dlq()
    {
        _sms.Falla = Caido;
        _correo.Falla = Caido;

        await Manejador().ManejarReintentoAsync(DeReintento(new(Cambio(), CanalesV1.Sms, 3)), CancellationToken.None);
        await Manejador().ManejarReintentoAsync(DeReintento(new(Cambio(), CanalesV1.Correo, 3)), CancellationToken.None);

        Assert.Equal((CanalesV1.Correo, 1), (_enrutador.Reintentos[0].Canal, _enrutador.Reintentos[0].Intento));
        Assert.Contains("Reintentos agotados", Assert.Single(_enrutador.Dlq).Motivo);
    }

    [Fact]
    public async Task Sin_telefono_va_directo_al_correo()
    {
        await Manejador(new Contacto(null, "c@correo.co")).ManejarCambioAsync(DeCambio(Cambio()), CancellationToken.None);

        Assert.Empty(_sms.Enviados);
        Assert.Single(_correo.Enviados);
    }

    [Fact]
    public async Task Un_destino_rechazado_no_se_reintenta_por_el_mismo_canal()
    {
        _sms.Falla = () => new DestinoRechazadoException("número inválido");

        await Manejador().ManejarCambioAsync(DeCambio(Cambio()), CancellationToken.None);

        Assert.Empty(_enrutador.Reintentos);
        Assert.Single(_correo.Enviados);
    }

    [Fact]
    public async Task Un_mensaje_ilegible_va_a_la_dlq()
    {
        await Manejador().ManejarCambioAsync(new MensajeKafka("t", 0, 1, "TCC1", "{roto"), CancellationToken.None);

        Assert.Equal("{roto", Assert.Single(_enrutador.Dlq).Contenido);
    }

    [Fact]
    public async Task Si_kafka_no_acepta_el_reintento_se_insiste_hasta_lograrlo()
    {
        _sms.Falla = Caido;
        _enrutador.FallasPendientes = 2;

        await Manejador().ManejarCambioAsync(DeCambio(Cambio()), CancellationToken.None);

        Assert.Single(_enrutador.Reintentos);
    }
}
