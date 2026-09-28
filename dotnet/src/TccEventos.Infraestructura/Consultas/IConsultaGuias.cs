using TccEventos.Contratos.V1;

namespace TccEventos.Infraestructura.Consultas;

/// Lado de lectura (CQRS): devuelve el contrato directamente, sin pasar por el dominio,
/// porque leer no cambia nada. Hoy lee PostgreSQL; a escala, una proyección en Redis.
public interface IConsultaGuias
{
    Task<GuiaV1?> ObtenerAsync(string numeroGuia, CancellationToken ct);
}
