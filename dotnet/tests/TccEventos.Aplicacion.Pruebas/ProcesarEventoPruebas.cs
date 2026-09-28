using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Pruebas.Falsos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas;

public class ProcesarEventoPruebas
{
    private const string Numero = "TCC123";
    private static readonly DateTimeOffset Hora = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(-5));

    private readonly RepositorioGuiasEnMemoria _repositorio = new();
    private readonly ProcesarEvento _casoUso;

    public ProcesarEventoPruebas() => _casoUso = new ProcesarEvento(_repositorio);

    private static EventoGuia Evento(EstadoGuia estado, DateTimeOffset cuando) =>
        new(Guid.NewGuid(), Numero, estado, cuando, "TMS");

    [Fact]
    public async Task El_primer_evento_crea_la_guia()
    {
        var resultado = await _casoUso.EjecutarAsync(Evento(EstadoGuia.Recogida, Hora), CancellationToken.None);

        Assert.Equal(ResultadoProcesamiento.Aplicado, resultado);
        Assert.Equal(EstadoGuia.Recogida, _repositorio.Guias[Numero].EstadoActual);
    }

    [Fact]
    public async Task Un_evento_ya_procesado_se_reconoce_como_duplicado()
    {
        var evento = Evento(EstadoGuia.Recogida, Hora);
        await _casoUso.EjecutarAsync(evento, CancellationToken.None);

        var resultado = await _casoUso.EjecutarAsync(evento, CancellationToken.None);

        Assert.Equal(ResultadoProcesamiento.Duplicado, resultado);
        Assert.Single(_repositorio.Historial);
    }

    [Fact]
    public async Task Un_evento_tardio_queda_en_historial_sin_cambiar_el_estado()
    {
        await _casoUso.EjecutarAsync(Evento(EstadoGuia.EnReparto, Hora), CancellationToken.None);

        var resultado = await _casoUso.EjecutarAsync(Evento(EstadoGuia.Recogida, Hora.AddHours(-3)), CancellationToken.None);

        Assert.Equal(ResultadoProcesamiento.Tardio, resultado);
        Assert.Equal(2, _repositorio.Historial.Count);
        Assert.Equal(EstadoGuia.EnReparto, _repositorio.Guias[Numero].EstadoActual);
    }

    [Fact]
    public async Task Una_transicion_invalida_queda_en_historial_sin_publicar_cambio()
    {
        await _casoUso.EjecutarAsync(Evento(EstadoGuia.Creada, Hora), CancellationToken.None);

        var resultado = await _casoUso.EjecutarAsync(Evento(EstadoGuia.Entregada, Hora.AddHours(1)), CancellationToken.None);

        Assert.Equal(ResultadoProcesamiento.TransicionInvalida, resultado);
        Assert.Equal(2, _repositorio.Historial.Count);
        Assert.Single(_repositorio.Cambios);
    }

    [Fact]
    public async Task Cada_cambio_aplicado_lleva_el_estado_anterior_y_una_version_consecutiva()
    {
        await _casoUso.EjecutarAsync(Evento(EstadoGuia.EnBodegaDestino, Hora), CancellationToken.None);
        await _casoUso.EjecutarAsync(Evento(EstadoGuia.EnReparto, Hora.AddHours(1)), CancellationToken.None);
        await _casoUso.EjecutarAsync(Evento(EstadoGuia.Entregada, Hora.AddHours(2)), CancellationToken.None);

        Assert.Equal(
            [
                (null, EstadoGuia.EnBodegaDestino, 1L),
                (EstadoGuia.EnBodegaDestino, EstadoGuia.EnReparto, 2L),
                (EstadoGuia.EnReparto, EstadoGuia.Entregada, 3L)
            ],
            _repositorio.Cambios);
    }
}