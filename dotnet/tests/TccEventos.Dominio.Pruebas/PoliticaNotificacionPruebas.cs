using TccEventos.Dominio;

namespace TccEventos.Dominio.Pruebas;

public class PoliticaNotificacionPruebas
{
    [Theory]
    [InlineData(EstadoGuia.Recogida, true)]
    [InlineData(EstadoGuia.EnReparto, true)]
    [InlineData(EstadoGuia.Entregada, true)]
    [InlineData(EstadoGuia.Novedad, true)]
    [InlineData(EstadoGuia.Devuelta, true)]
    [InlineData(EstadoGuia.EnBodegaOrigen, false)]
    [InlineData(EstadoGuia.EnTransito, false)]
    public void Solo_notifica_lo_que_le_importa_al_cliente(EstadoGuia estado, bool esperado) =>
        Assert.Equal(esperado, PoliticaNotificacion.DebeNotificar(estado));

    [Fact]
    public void Todo_estado_que_notifica_tiene_texto()
    {
        foreach (var estado in Enum.GetValues<EstadoGuia>().Where(PoliticaNotificacion.DebeNotificar))
            Assert.Contains("TCC123",
                PoliticaNotificacion.Texto(new CambioEstadoGuia(Guid.NewGuid(), "TCC123", null, estado, DateTimeOffset.UnixEpoch, 1)));
    }

    [Fact]
    public void La_clave_de_idempotencia_distingue_version_y_canal()
    {
        var cambio = new CambioEstadoGuia(Guid.NewGuid(), "TCC123", EstadoGuia.EnReparto, EstadoGuia.Entregada, DateTimeOffset.UnixEpoch, 7);

        Assert.Equal("TCC123:7:SMS", PoliticaNotificacion.ClaveIdempotencia(cambio, CanalNotificacion.Sms));
        Assert.Equal("TCC123:7:CORREO", PoliticaNotificacion.ClaveIdempotencia(cambio, CanalNotificacion.Correo));
    }

    [Fact]
    public void El_correo_es_el_alterno_del_sms_y_no_tiene_alterno()
    {
        Assert.Equal(CanalNotificacion.Correo, PoliticaNotificacion.CanalAlterno(CanalNotificacion.Sms));
        Assert.Null(PoliticaNotificacion.CanalAlterno(CanalNotificacion.Correo));
    }
}
