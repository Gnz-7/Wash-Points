namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Lavadero (comercio). Mínimo para resolver las FK de este slice:
/// ver DOCS/ARCHITECTURE.md §10.7 — el módulo Catalogo aún no está modelado.
/// </summary>
public sealed class Lavadero
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Lavadero()
    {
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public bool Activo { get; private set; } = true;

    /// <summary>Dirección que se muestra en el buscador (CA-7.2).</summary>
    public string? Direccion { get; private set; }

    /// <summary>Latitud en grados decimales; null si no está ubicado en el mapa.</summary>
    public double? Latitud { get; private set; }

    /// <summary>Longitud en grados decimales; null si no está ubicado en el mapa.</summary>
    public double? Longitud { get; private set; }

    public static Lavadero Crear(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        return new Lavadero { Id = Guid.NewGuid(), Nombre = nombre.Trim() };
    }

    /// <summary>
    /// Fija la ubicación del lavadero. Las dos coordenadas van juntas: un
    /// lavadero a medio ubicar no puede aparecer en la búsqueda por
    /// proximidad (D-08.1).
    /// </summary>
    public void Ubicar(string? direccion, double latitud, double longitud)
    {
        if (double.IsNaN(latitud) || latitud is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitud), "La latitud debe estar entre -90 y 90.");
        }

        if (double.IsNaN(longitud) || longitud is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitud), "La longitud debe estar entre -180 y 180.");
        }

        Direccion = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();
        Latitud = latitud;
        Longitud = longitud;
    }
}
