using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Infraestructura.Kafka;
using TccEventos.Infraestructura.Resiliencia;
using TccEventos.Procesador.Consumo;
using TccEventos.Procesador.PruebasIntegracion.Falsos;

namespace TccEventos.Procesador.PruebasIntegracion;

public class ManejadorMensajeRecibidoPruebas
{
    // Esperas de 1 ms: se prueba la política, no el reloj.
    private static readonly OpcionesConsumidor Opciones = new() { IntentosErrorInesperado = 3, EsperaMinimaMs = 1, EsperaMaximaMs = 1 };

    private readonly RepositorioGuiasProgramable _repositorio = new();
    private readonly DlqEnMemoria _dlq = new();
    private readonly ManejadorMensajeRecibido _manejador;

    public ManejadorMensajeRecibidoPruebas() =>
        _manejador = new(new ProcesarEvento(_repositorio), _dlq, Opciones, NullLogger<ManejadorMensajeRecibido>.Instance);

    private static MensajeKafka Mensaje(string? valor) => new("guias.eventos.recibidos", 7, 42, "TCC123", valor);

    private static string EventoJson(Guid? id = null) =>
        $$"""{"idEvento":"{{id ?? Guid.NewGuid()}}","numeroGuia":"TCC123","estado":"EN_REPARTO","ocurridoEn":"2026-09-26T10:15:00-05:00","origen":"TMS"}""";

    [Fact]
    public async Task Un_evento_valido_se_procesa_y_no_va_a_la_dlq()
    {
        await _manejador.ManejarAsync(Mensaje(EventoJson()), CancellationToken.None);

        Assert.Single(_repositorio.Guardados);
        Assert.Empty(_dlq.Rechazados);
    }

    [Theory]
    [InlineData("esto no es json")]
    [InlineData("""{"idEvento":"0199a1b2-7c3d-7e4f-8a9b-0c1d2e3f4a5b","numeroGuia":"TCC1","estado":"PERDIDA","ocurridoEn":"2026-09-26T10:15:00-05:00","origen":"TMS"}""")]
    [InlineData("")]
    public async Task Un_mensaje_ilegible_va_a_la_dlq_sin_reintentos_y_conserva_su_origen(string valor)
    {
        await _manejador.ManejarAsync(Mensaje(valor), CancellationToken.None);

        var rechazado = Assert.Single(_dlq.Rechazados);
        Assert.Equal((7, 42L), (rechazado.Original.Particion, rechazado.Original.Offset));
        Assert.Equal(valor, rechazado.Original.Valor);
        Assert.Equal(0, _repositorio.IntentosDeGuardar);
    }

    [Fact]
    public async Task Un_error_transitorio_se_reintenta_hasta_que_funciona_sin_saltar_el_evento()
    {
        _repositorio.FallarProximas(10, () => new TimeoutException("PostgreSQL no responde"));

        await _manejador.ManejarAsync(Mensaje(EventoJson()), CancellationToken.None);

        Assert.Equal(11, _repositorio.IntentosDeGuardar);
        Assert.Single(_repositorio.Guardados);
        Assert.Empty(_dlq.Rechazados);
    }

    [Fact]
    public async Task Un_conflicto_de_concurrencia_se_reintenta()
    {
        _repositorio.FallarProximas(2, () => new ConflictoConcurrenciaException("TCC123"));

        await _manejador.ManejarAsync(Mensaje(EventoJson()), CancellationToken.None);

        Assert.Single(_repositorio.Guardados);
    }

    [Fact]
    public async Task Un_error_inesperado_va_a_la_dlq_tras_los_intentos_configurados()
    {
        _repositorio.FallarProximas(100, () => new InvalidOperationException("bug"));

        await _manejador.ManejarAsync(Mensaje(EventoJson()), CancellationToken.None);

        Assert.Equal(Opciones.IntentosErrorInesperado, _repositorio.IntentosDeGuardar);
        Assert.Contains("InvalidOperationException", Assert.Single(_dlq.Rechazados).Motivo);
    }

    [Fact]
    public async Task Si_la_dlq_falla_se_insiste_hasta_guardar_el_rechazado()
    {
        _dlq.FallasPendientes = 3;

        await _manejador.ManejarAsync(Mensaje("basura"), CancellationToken.None);

        Assert.Single(_dlq.Rechazados);
    }

    [Fact]
    public async Task Al_detener_el_servicio_se_interrumpe_el_reintento_sin_marcar_el_mensaje()
    {
        _repositorio.FallarProximas(int.MaxValue, () => new TimeoutException());
        using var cancelacion = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _manejador.ManejarAsync(Mensaje(EventoJson()), cancelacion.Token));
        Assert.Empty(_dlq.Rechazados);
    }
}

public class ClasificadorErroresPruebas
{
    public static TheoryData<Exception, bool> Casos => new()
    {
        { new ConflictoConcurrenciaException("TCC1"), true },
        { new TimeoutException(), true },
        { new PublicacionFallidaException("kafka"), true },
        { new NpgsqlException("sin conexión", new System.Net.Sockets.SocketException()), true },
        { new InvalidOperationException("envuelve", new TimeoutException()), true },
        { new InvalidOperationException("bug"), false },
        { new KeyNotFoundException(), false }
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public void Distingue_errores_transitorios_de_los_que_no_lo_son(Exception error, bool esperado) =>
        Assert.Equal(esperado, ClasificadorErrores.EsTransitorio(error));
}
