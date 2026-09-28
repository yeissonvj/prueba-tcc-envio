using Microsoft.Extensions.Logging.Abstractions;

using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Decoradores;
using TccEventos.Aplicacion.Pruebas.Falsos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas;

public class FiltroDuplicadosTolerantePruebas
{
    private readonly FiltroDuplicadosTolerante _filtro =
        new(new FiltroDuplicadosQueFalla(), NullLogger<FiltroDuplicadosTolerante>.Instance);

    [Fact]
    public async Task Si_el_filtro_falla_el_evento_se_considera_nuevo() =>
        Assert.False(await _filtro.YaRecibidoAsync(Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task Si_no_se_puede_marcar_no_se_propaga_el_error() =>
        await _filtro.MarcarRecibidoAsync(Guid.NewGuid(), CancellationToken.None);

    [Fact]
    public async Task Con_el_filtro_caido_la_recepcion_publica_igual()
    {
        var publicador = new PublicadorFalso();
        var casoUso = new RecibirEvento(publicador, _filtro);
        var evento = new EventoGuia(Guid.NewGuid(), "TCC123", EstadoGuia.Recogida, DateTimeOffset.UnixEpoch, "TMS");

        var resultado = await casoUso.EjecutarAsync(evento, CancellationToken.None);

        Assert.Equal(ResultadoRecepcion.Aceptado, resultado);
        Assert.Single(publicador.Publicados);
    }
}