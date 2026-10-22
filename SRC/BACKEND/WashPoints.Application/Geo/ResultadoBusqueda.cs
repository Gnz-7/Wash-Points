using WashPoints.Domain.Catalogo;

namespace WashPoints.Application.Geo;

/// <summary>
/// Resultado de una búsqueda por proximidad: el centro usado como referencia
/// (geolocalizado o geocodificado), el radio aplicado y los lavaderos
/// ordenados por distancia.
/// </summary>
public sealed record ResultadoBusqueda(
    double Latitud,
    double Longitud,
    int RadioMetros,
    IReadOnlyList<LavaderoCercano> Resultados);
