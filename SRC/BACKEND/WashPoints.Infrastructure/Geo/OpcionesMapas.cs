namespace WashPoints.Infrastructure.Geo;

/// <summary>
/// Configuración del proveedor de mapas (sección <c>Mapas</c>). La clave vive
/// sólo del lado servidor: el cliente usa la suya para las teselas (D-16.7).
/// </summary>
public sealed class OpcionesMapas
{
    public const string Seccion = "Mapas";

    /// <summary>Clave de MapTiler para geocodificación.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Base del endpoint de geocodificación.</summary>
    public string UrlBase { get; set; } = "https://api.maptiler.com/geocoding/";

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(ApiKey);
}
