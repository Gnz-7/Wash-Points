namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Lectura del catálogo necesaria para armar una reserva (SPECS/slice-01 T-13).
///
/// El servidor resuelve el <c>precio_sena</c> y la duración desde el catálogo;
/// el cliente nunca los envía (RB-1: la seña no se decide en el cliente).
/// </summary>
public interface IConsultaCatalogo
{
    Task<TipoLavado?> ObtenerTipoLavadoAsync(Guid tipoLavadoId, CancellationToken ct);
}
