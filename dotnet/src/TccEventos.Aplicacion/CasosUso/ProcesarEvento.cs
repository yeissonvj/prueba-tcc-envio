using System.Diagnostics;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.CasosUso;

/// <summary>
/// Aplica un evento a su guía y guarda el resultado. Lo usa el procesador de estado.
/// </summary>
public class ProcesarEvento(IRepositorioGuias repositorio)
{
    public async Task<ResultadoProcesamiento> EjecutarAsync(EventoGuia evento, CancellationToken ct)
    {
        if (await repositorio.ExisteEventoAsync(evento.IdEvento, ct))
            return ResultadoProcesamiento.Duplicado;

        var guia = await repositorio.ObtenerAsync(evento.NumeroGuia, ct);
        var estadoAnterior = guia?.EstadoActual;

        ResultadoAplicacion resultado;
        if (guia is null)
        {
            guia = Guia.Crear(evento);
            resultado = ResultadoAplicacion.Aplicado;
        }
        else
        {
            resultado = guia.Aplicar(evento);
        }

        await repositorio.GuardarAsync(guia, evento, resultado, estadoAnterior, ct);

        return AResultadoProcesamiento(resultado);
    }

    // Conversión explícita: no depende del orden de los valores de los dos enums.
    private static ResultadoProcesamiento AResultadoProcesamiento(ResultadoAplicacion resultado) => resultado switch
    {
        ResultadoAplicacion.Aplicado => ResultadoProcesamiento.Aplicado,
        ResultadoAplicacion.Tardio => ResultadoProcesamiento.Tardio,
        ResultadoAplicacion.TransicionInvalida => ResultadoProcesamiento.TransicionInvalida,
        _ => throw new UnreachableException($"Resultado de aplicación no contemplado: {resultado}")
    };
}
