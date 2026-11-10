namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Lectura del catálogo necesaria para armar una reserva (SPECS/slice-01 T-13).
///
/// El servidor resuelve el <c>precio_sena</c> y la duración desde el catálogo;
/// el cliente nunca los envía (RB-1: la seña no se decide en el cliente).
/// </summary>
public interface IConsultaCatalogo
{
    /// <summary>
    /// Tipo de lavado del lavadero indicado, activo. Un id de otro comercio se
    /// trata como inexistente (slice-17, D-17.8): nunca se cobra la seña de un
    /// tipo ajeno.
    /// </summary>
    Task<TipoLavado?> ObtenerTipoLavadoAsync(
        Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct);

    /// <summary>
    /// Puesto del lavadero indicado, activo (slice-17, D-17.8). Un puesto de
    /// otro comercio se trata como inexistente.
    /// </summary>
    Task<Puesto?> ObtenerPuestoAsync(
        Guid lavaderoId, Guid puestoId, CancellationToken ct);

    /// <summary>
    /// ¿El puesto atiende ese tipo de lavado? (slice-17, D-17.7 y D-17.10).
    /// Sin filas de compatibilidad, el puesto atiende todos los servicios.
    /// </summary>
    Task<bool> PuestoAtiendeTipoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, CancellationToken ct);

    /// <summary>
    /// Catálogo del lavadero para que el cliente arme una reserva
    /// (slice-09, T-95). Devuelve <c>null</c> si el lavadero no existe; solo
    /// incluye puestos y tipos activos.
    /// </summary>
    Task<CatalogoLavadero?> ObtenerCatalogoAsync(Guid lavaderoId, CancellationToken ct);
}
