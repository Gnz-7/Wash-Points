namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Catálogo que el cliente necesita para reservar (SPECS/slice-09, T-95):
/// puestos y tipos de lavado activos de un lavadero, con el precio de la seña
/// que el servidor va a cobrar. El cliente nunca decide el precio (RB-1).
///
/// Desde el slice-17 (D-17.10) también publica qué tipos atiende cada puesto.
/// Un puesto sin filas en el mapa atiende todos los servicios (D-17.7).
/// </summary>
public sealed record CatalogoLavadero(
    IReadOnlyList<Puesto> Puestos,
    IReadOnlyList<TipoLavado> Tipos,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>? TiposPorPuesto = null)
{
    /// <summary>
    /// Tipos compatibles con el puesto. Lista vacía significa «todos los
    /// servicios», que es el comportamiento de un puesto sin restricción.
    /// </summary>
    public IReadOnlyList<Guid> TiposDe(Guid puestoId) =>
        TiposPorPuesto is not null && TiposPorPuesto.TryGetValue(puestoId, out var tipos)
            ? tipos
            : Array.Empty<Guid>();
}
