using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;
using TccEventos.Infraestructura.Postgres;

namespace TccEventos.Procesador.PruebasIntegracion.Integracion;

[Collection(ColeccionInfraestructura.Nombre)]
public class RepositorioGuiasPostgresPruebas(InfraestructuraReal infra) : IAsyncLifetime
{
    private static readonly DateTimeOffset Hora = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(-5));

    private RepositorioGuiasPostgres Repositorio => new(infra.BaseDatos);
    private ProcesarEvento CasoUso => new(Repositorio);

    public Task InitializeAsync() => infra.LimpiarAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static EventoGuia Evento(string guia, EstadoGuia estado, DateTimeOffset cuando) =>
        new(Guid.NewGuid(), guia, estado, cuando, "TMS");

    [Fact]
    public async Task Cada_cambio_aplicado_deja_historial_estado_y_mensaje_en_la_bandeja_en_una_transaccion()
    {
        await CasoUso.EjecutarAsync(Evento("TCC1", EstadoGuia.EnBodegaDestino, Hora), default);
        await CasoUso.EjecutarAsync(Evento("TCC1", EstadoGuia.EnReparto, Hora.AddHours(1)), default);

        var guia = await Repositorio.ObtenerAsync("TCC1", default);
        Assert.Equal((EstadoGuia.EnReparto, 2L), (guia!.EstadoActual, guia.Version));
        Assert.Equal(Hora.AddHours(1), guia.UltimoEventoEn);
        Assert.Equal(2, await infra.ContarAsync("SELECT count(*) FROM historial_eventos WHERE numero_guia = 'TCC1'"));
        Assert.Equal(2, await infra.ContarAsync("SELECT count(*) FROM bandeja_salida WHERE numero_guia = 'TCC1'"));
    }

    [Fact]
    public async Task Cincuenta_procesamientos_concurrentes_del_mismo_evento_producen_un_solo_efecto()
    {
        var evento = Evento("TCC2", EstadoGuia.Recogida, Hora);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => ProcesarConReintentosAsync(evento))));

        Assert.Single(resultados, r => r == ResultadoProcesamiento.Aplicado);
        Assert.Equal(49, resultados.Count(r => r == ResultadoProcesamiento.Duplicado));
        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM historial_eventos WHERE id_evento = $1", evento.IdEvento));
        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM bandeja_salida WHERE numero_guia = 'TCC2'"));
        Assert.Equal(1, await infra.ContarAsync("SELECT version FROM guias WHERE numero_guia = 'TCC2'"));
    }

    [Fact]
    public async Task Una_escritura_sobre_una_version_vieja_falla_y_no_deja_nada_a_medias()
    {
        await CasoUso.EjecutarAsync(Evento("TCC3", EstadoGuia.EnBodegaDestino, Hora), default);
        var copiaA = await Repositorio.ObtenerAsync("TCC3", default);
        var copiaB = await Repositorio.ObtenerAsync("TCC3", default);

        var eventoA = Evento("TCC3", EstadoGuia.EnReparto, Hora.AddHours(1));
        copiaA!.Aplicar(eventoA);
        await Repositorio.GuardarAsync(copiaA, eventoA, ResultadoAplicacion.Aplicado, EstadoGuia.EnBodegaDestino, default);

        var eventoB = Evento("TCC3", EstadoGuia.Novedad, Hora.AddHours(1));
        copiaB!.Aplicar(eventoB);
        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(() =>
            Repositorio.GuardarAsync(copiaB, eventoB, ResultadoAplicacion.Aplicado, EstadoGuia.EnBodegaDestino, default));

        // Rollback completo: ni historial ni bandeja del evento perdedor.
        Assert.Equal(0, await infra.ContarAsync("SELECT count(*) FROM historial_eventos WHERE id_evento = $1", eventoB.IdEvento));
        Assert.Equal(2, await infra.ContarAsync("SELECT count(*) FROM bandeja_salida WHERE numero_guia = 'TCC3'"));
        Assert.Equal(EstadoGuia.EnReparto, (await Repositorio.ObtenerAsync("TCC3", default))!.EstadoActual);
    }

    [Theory]
    [InlineData(EstadoGuia.Recogida, -3, "TARDIO")]
    [InlineData(EstadoGuia.Creada, 3, "TRANSICION_INVALIDA")]
    public async Task Tardios_e_invalidos_quedan_en_historial_sin_cambiar_estado_ni_publicar(
        EstadoGuia estado, int horas, string resultadoEsperado)
    {
        await CasoUso.EjecutarAsync(Evento("TCC4", EstadoGuia.EnReparto, Hora), default);

        await CasoUso.EjecutarAsync(Evento("TCC4", estado, Hora.AddHours(horas)), default);

        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM historial_eventos WHERE resultado = $1", resultadoEsperado));
        Assert.Equal(1, await infra.ContarAsync("SELECT count(*) FROM bandeja_salida"));
        Assert.Equal(EstadoGuia.EnReparto, (await Repositorio.ObtenerAsync("TCC4", default))!.EstadoActual);
    }

    // Lo mismo que hace el consumidor real: un conflicto se reintenta y el inbox lo resuelve.
    private async Task<ResultadoProcesamiento> ProcesarConReintentosAsync(EventoGuia evento)
    {
        while (true)
        {
            try
            {
                return await CasoUso.EjecutarAsync(evento, default);
            }
            catch (ConflictoConcurrenciaException)
            {
            }
        }
    }
}
