using System.Diagnostics;
using TccEventos.Aplicacion.Puertos;
using TccEventos.Dominio;

namespace TccEventos.Aplicacion.CasosUso;

/// <summary>
/// Caso de uso: aplica un evento a su guía y guarda el resultado. Lo usa el procesador de estado.
/// </summary>
/// <param name="repositorio">Repositorio de guías (inbox, estado y bandeja de salida).</param>
public class ProcesarEvento(IRepositorioGuias repositorio)
{
    /// <summary>
    /// Procesa un evento: descarta los duplicados, crea o carga la guía, le aplica el evento
    /// y guarda historial, estado y cambio en una sola transacción.
    /// </summary>
    /// <param name="evento">Evento leído de guias.eventos.recibidos.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Qué pasó con el evento: aplicado, tardío, transición inválida o duplicado.</returns>
    /// <exception cref="ConflictoConcurrenciaException">Otra instancia escribió la guía al mismo tiempo; se reintenta.</exception>
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
    /// <summary>Traduce el resultado del dominio al resultado del caso de uso.</summary>
    /// <param name="resultado">Resultado de aplicar el evento a la guía.</param>
    /// <returns>El resultado equivalente del procesamiento.</returns>
    /// <exception cref="UnreachableException">Si aparece un resultado nuevo sin traducción.</exception>
    private static ResultadoProcesamiento AResultadoProcesamiento(ResultadoAplicacion resultado) => resultado switch
    {
        ResultadoAplicacion.Aplicado => ResultadoProcesamiento.Aplicado,
        ResultadoAplicacion.Tardio => ResultadoProcesamiento.Tardio,
        ResultadoAplicacion.TransicionInvalida => ResultadoProcesamiento.TransicionInvalida,
        _ => throw new UnreachableException($"Resultado de aplicación no contemplado: {resultado}")
    };
}
