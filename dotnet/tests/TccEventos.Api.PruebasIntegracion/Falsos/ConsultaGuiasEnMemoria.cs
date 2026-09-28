using System.Collections.Concurrent;
using TccEventos.Contratos.V1;
using TccEventos.Infraestructura.Consultas;

namespace TccEventos.Api.PruebasIntegracion.Falsos;

public class ConsultaGuiasEnMemoria : IConsultaGuias
{
    public ConcurrentDictionary<string, GuiaV1> Guias { get; } = new();
    public int Consultas { get; private set; }

    public Task<GuiaV1?> ObtenerAsync(string numeroGuia, CancellationToken ct)
    {
        Consultas++;
        return Task.FromResult(Guias.GetValueOrDefault(numeroGuia));
    }
}
