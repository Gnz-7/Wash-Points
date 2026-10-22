namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Búsqueda de lavaderos por proximidad. La resuelve la base local con la
/// fórmula de Haversine sobre las coordenadas de <see cref="Lavadero"/>:
/// el proveedor de mapas no participa (D-08.2).
/// </summary>
public interface IBusquedaLavaderos
{
    /// <summary>
    /// Lavaderos activos ubicados dentro del radio, ordenados por distancia
    /// creciente, con el resumen de ocupación vigente en <paramref name="ahoraUtc"/>.
    /// </summary>
    Task<IReadOnlyList<LavaderoCercano>> BuscarCercanosAsync(
        double latitud,
        double longitud,
        int radioMetros,
        DateTime ahoraUtc,
        CancellationToken ct = default);
}
