namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puerto de persistencia de puestos de lavado (SPECS/slice-04 T-42, CU-05).
///
/// Todo lo que escribe en la tabla `puestos` pasa por acá. Igual que
/// <see cref="Ocupacion.IOutbox"/> y <see cref="IRegistroClientes"/>, no dispara
/// su propio `SaveChangesAsync`: escribe por el Change Tracker de la unidad de
/// trabajo (<see cref="GuardarAsync"/>).
/// </summary>
public interface IPuestoRepositorio
{
    Task AgregarAsync(Puesto puesto, CancellationToken ct);

    Task<Puesto?> ObtenerAsync(Guid lavaderoId, Guid puestoId, CancellationToken ct);

    Task<IReadOnlyList<Puesto>> ListarAsync(Guid lavaderoId, CancellationToken ct);

    /// <summary>
    /// Tipos de lavado compatibles con el puesto (slice-17, D-17.7). Una lista
    /// vacía significa «todos los servicios».
    /// </summary>
    Task<IReadOnlyList<Guid>> ObtenerTiposAsync(Guid puestoId, CancellationToken ct);

    /// <summary>
    /// Tipos compatibles de todos los puestos del lavadero, para el catálogo
    /// (D-17.10). Los puestos sin restricción no aparecen en el diccionario.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ObtenerTiposPorPuestoAsync(
        Guid lavaderoId, CancellationToken ct);

    /// <summary>
    /// Reemplaza el conjunto de tipos compatibles del puesto. Lista vacía deja
    /// el puesto atendiendo todos los servicios.
    /// </summary>
    Task ReemplazarTiposAsync(
        Guid puestoId, IReadOnlyList<Guid> tiposLavadoIds, CancellationToken ct);

    Task GuardarAsync(CancellationToken ct);
}
