using TccEventos.Api.PruebasIntegracion.Falsos;
using TccEventos.Api.Validacion;
using TccEventos.Contratos.V1;

namespace TccEventos.Api.PruebasIntegracion;

public class ValidadorEventoGuiaV1Pruebas
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 30, 10, 0, 0, TimeSpan.FromHours(-5));
    private readonly ValidadorEventoGuiaV1 _validador = new(new RelojFijo(Ahora));

    private static EventoGuiaV1 EventoValido() =>
        new(Guid.NewGuid(), "TCC123456789", EstadosV1.EnReparto, Ahora.AddMinutes(-1), "TMS");

    [Fact]
    public void Un_evento_valido_no_tiene_errores() =>
        Assert.Empty(_validador.Validar(EventoValido()));

    [Fact]
    public void Rechaza_id_de_evento_vacio() =>
        Assert.Contains("idEvento", _validador.Validar(EventoValido() with { IdEvento = Guid.Empty }).Keys);

    [Theory]
    [InlineData("")]
    [InlineData("TCC-123")]
    [InlineData("TCC1234567890123456789012345678901")]
    public void Rechaza_numeros_de_guia_mal_formados(string numero) =>
        Assert.Contains("numeroGuia", _validador.Validar(EventoValido() with { NumeroGuia = numero }).Keys);

    [Fact]
    public void Rechaza_estados_que_no_estan_en_el_contrato() =>
        Assert.Contains("estado", _validador.Validar(EventoValido() with { Estado = "PERDIDA" }).Keys);

    [Fact]
    public void Rechaza_eventos_en_el_futuro_mas_alla_de_la_tolerancia() =>
        Assert.Contains("ocurridoEn", _validador.Validar(EventoValido() with { OcurridoEn = Ahora.AddMinutes(10) }).Keys);

    [Fact]
    public void Acepta_desfase_de_reloj_dentro_de_la_tolerancia() =>
        Assert.Empty(_validador.Validar(EventoValido() with { OcurridoEn = Ahora.AddMinutes(3) }));

    [Fact]
    public void Una_novedad_exige_descripcion() =>
        Assert.Contains("novedad", _validador.Validar(EventoValido() with { Estado = EstadosV1.Novedad }).Keys);

    [Fact]
    public void Reporta_todos_los_errores_a_la_vez()
    {
        var errores = _validador.Validar(new EventoGuiaV1(Guid.Empty, "", "", default, ""));

        Assert.Equal(["estado", "idEvento", "numeroGuia", "ocurridoEn", "origen"], errores.Keys.Order());
    }
}
