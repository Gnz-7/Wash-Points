using WashPoints.Domain.Catalogo;

namespace WashPoints.Application.Geo;

/// <summary>
/// Resultado de una búsqueda por proximidad: el centro usado como referencia
/// (geolocalizado o geocodificado), el radio aplicado y los lavaderos
/// ordenados por distancia.
/// </summary>
/// <param name="Modo">
/// Cómo se resolvió la búsqueda: <c>ubicacion</c> (coordenadas del cliente),
/// <c>direccion</c> (texto geocodificado) o <c>nombre</c> (coincidencia por
/// nombre o dirección, D-21.17).
/// </param>
public sealed record ResultadoBusqueda(
    double Latitud,
    double Longitud,
    int RadioMetros,
    IReadOnlyList<LavaderoCercano> Resultados,
    string Modo = "ubicacion");
