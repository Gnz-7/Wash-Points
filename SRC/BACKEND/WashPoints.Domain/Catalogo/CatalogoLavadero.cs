namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Catálogo que el cliente necesita para reservar (SPECS/slice-09, T-95):
/// puestos y tipos de lavado activos de un lavadero, con el precio de la seña
/// que el servidor va a cobrar. El cliente nunca decide el precio (RB-1).
/// </summary>
public sealed record CatalogoLavadero(
    IReadOnlyList<Puesto> Puestos,
    IReadOnlyList<TipoLavado> Tipos);
