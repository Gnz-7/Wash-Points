namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Lavadero dentro del radio de búsqueda, con lo mínimo que muestra el
/// buscador (CA-7.2): identificación, dirección, distancia y disponibilidad
/// resumida del momento.
/// </summary>
public sealed record LavaderoCercano(
    Guid LavaderoId,
    string Nombre,
    string? Direccion,
    double DistanciaMetros,
    int PuestosTotales,
    int PuestosActivos,
    int OcupadosAhora);
