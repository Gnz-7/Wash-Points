namespace WashPoints.Domain.Geo;

/// <summary>
/// Geocodificación de texto a coordenadas. Es el único punto del sistema que
/// depende de un proveedor de mapas; la búsqueda por proximidad no pasa por
/// acá (D-08.3/D-08.4).
/// </summary>
public interface IGeocodificador
{
    /// <summary>
    /// Coordenadas de la dirección indicada, o <c>null</c> si el proveedor no
    /// la reconoce. Si el proveedor no está configurado lanza
    /// <see cref="ProveedorMapasNoConfiguradoException"/>: nunca devuelve un
    /// resultado vacío como si la dirección no existiera (CA-7.4).
    /// </summary>
    Task<Coordenadas?> GeocodificarAsync(string direccion, CancellationToken ct = default);
}
