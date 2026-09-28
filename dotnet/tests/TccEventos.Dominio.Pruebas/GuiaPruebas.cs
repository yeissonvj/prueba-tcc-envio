using TccEventos.Dominio;

namespace TccEventos.Dominio.Pruebas;

public class GuiaPruebas
{
    private const string Numero = "TCC123456789";
    private static readonly DateTimeOffset Hora = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(-5));

    private static EventoGuia Evento(EstadoGuia estado, DateTimeOffset cuando) =>
        new(Guid.NewGuid(), Numero, estado, cuando, "TMS");

    [Fact]
    public void Aplica_un_cambio_valido_y_sube_la_version()
    {
        var guia = Guia.Crear(Evento(EstadoGuia.EnBodegaDestino, Hora));

        var resultado = guia.Aplicar(Evento(EstadoGuia.EnReparto, Hora.AddHours(1)));

        Assert.Equal(ResultadoAplicacion.Aplicado, resultado);
        Assert.Equal(EstadoGuia.EnReparto, guia.EstadoActual);
        Assert.Equal(2, guia.Version);
    }

    [Fact]
    public void Un_evento_tardio_no_cambia_el_estado()
    {
        var guia = Guia.Crear(Evento(EstadoGuia.EnReparto, Hora));
        guia.Aplicar(Evento(EstadoGuia.Entregada, Hora.AddHours(2)));

        var resultado = guia.Aplicar(Evento(EstadoGuia.Recogida, Hora.AddHours(-5)));

        Assert.Equal(ResultadoAplicacion.Tardio, resultado);
        Assert.Equal(EstadoGuia.Entregada, guia.EstadoActual);
    }

    [Fact]
    public void Una_transicion_invalida_no_cambia_el_estado()
    {
        var guia = Guia.Crear(Evento(EstadoGuia.Creada, Hora));

        var resultado = guia.Aplicar(Evento(EstadoGuia.Entregada, Hora.AddHours(1)));

        Assert.Equal(ResultadoAplicacion.TransicionInvalida, resultado);
        Assert.Equal(EstadoGuia.Creada, guia.EstadoActual);
        Assert.Equal(1, guia.Version);
    }

    [Fact]
    public void Una_guia_entregada_no_acepta_mas_cambios()
    {
        var guia = Guia.Crear(Evento(EstadoGuia.EnReparto, Hora));
        guia.Aplicar(Evento(EstadoGuia.Entregada, Hora.AddHours(1)));

        var resultado = guia.Aplicar(Evento(EstadoGuia.Novedad, Hora.AddHours(2)));

        Assert.Equal(ResultadoAplicacion.TransicionInvalida, resultado);
    }

    [Fact]
    public void Rechaza_eventos_de_otra_guia()
    {
        var guia = Guia.Crear(Evento(EstadoGuia.Creada, Hora));
        var eventoAjeno = new EventoGuia(Guid.NewGuid(), "OTRA", EstadoGuia.Recogida, Hora, "TMS");

        Assert.Throws<ArgumentException>(() => guia.Aplicar(eventoAjeno));
    }
}