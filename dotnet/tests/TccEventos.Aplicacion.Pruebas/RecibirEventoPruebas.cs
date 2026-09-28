using TccEventos.Aplicacion.CasosUso;
using TccEventos.Aplicacion.Pruebas.Falsos;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.Pruebas;

public class RecibirEventoPruebas
{
    private readonly PublicadorFalso _publicador = new();
    private readonly FiltroDuplicadosEnMemoria _filtro = new();
    private readonly RecibirEvento _casoUso;

    public RecibirEventoPruebas() => _casoUso = new RecibirEvento(_publicador, _filtro);

    private static EventoGuia NuevoEvento() =>
        new(Guid.NewGuid(), "TCC123", EstadoGuia.Recogida, DateTimeOffset.UnixEpoch, "TMS");

    [Fact]
    public async Task Un_evento_nuevo_se_publica_y_se_acepta()
    {
        var evento = NuevoEvento();

        var resultado = await _casoUso.EjecutarAsync(evento, CancellationToken.None);

        Assert.Equal(ResultadoRecepcion.Aceptado, resultado);
        Assert.Single(_publicador.Publicados);
    }

    [Fact]
    public async Task Un_evento_repetido_no_se_publica_dos_veces()
    {
        var evento = NuevoEvento();
        await _casoUso.EjecutarAsync(evento, CancellationToken.None);

        var resultado = await _casoUso.EjecutarAsync(evento, CancellationToken.None);

        Assert.Equal(ResultadoRecepcion.Duplicado, resultado);
        Assert.Single(_publicador.Publicados);
    }

    [Fact]
    public async Task Si_la_publicacion_falla_el_reintento_no_se_descarta()
    {
        var evento = NuevoEvento();
        _publicador.Fallar = true;
        await Assert.ThrowsAsync<PublicacionFallidaException>(
            () => _casoUso.EjecutarAsync(evento, CancellationToken.None));

        _publicador.Fallar = false;
        var resultado = await _casoUso.EjecutarAsync(evento, CancellationToken.None);

        Assert.Equal(ResultadoRecepcion.Aceptado, resultado);
        Assert.Single(_publicador.Publicados);
    }
}