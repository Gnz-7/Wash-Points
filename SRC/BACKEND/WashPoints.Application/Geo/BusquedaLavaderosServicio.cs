using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Geo;

namespace WashPoints.Application.Geo;

/// <summary>
/// Orquesta la búsqueda por proximidad (CU-01). Valida los parámetros y, en el
/// modo dirección, traduce el texto a coordenadas con el puerto
/// <see cref="IGeocodificador"/>; el cálculo de distancias y la ocupación los
/// resuelve <see cref="IBusquedaLavaderos"/> (D-08.3).
/// </summary>
public sealed class BusquedaLavaderosServicio
{
    /// <summary>Radio que se aplica cuando el cliente no lo envía.</summary>
    public const int RadioPorDefectoMetros = 5000;

    /// <summary>Radio mínimo admitido: por debajo, el resultado no es útil.</summary>
    public const int RadioMinimoMetros = 100;

    /// <summary>Radio máximo admitido: acota el costo de la consulta.</summary>
    public const int RadioMaximoMetros = 50_000;

    /// <summary>Cantidad máxima de coincidencias por nombre (D-21.17).</summary>
    public const int LimitePorTexto = 20;

    private readonly IBusquedaLavaderos _busqueda;
    private readonly IGeocodificador _geocodificador;

    public BusquedaLavaderosServicio(IBusquedaLavaderos busqueda, IGeocodificador geocodificador)
    {
        _busqueda = busqueda ?? throw new ArgumentNullException(nameof(busqueda));
        _geocodificador = geocodificador ?? throw new ArgumentNullException(nameof(geocodificador));
    }

    /// <summary>Búsqueda directa con las coordenadas que envía el cliente.</summary>
    public async Task<ResultadoBusqueda> BuscarPorUbicacionAsync(
        double latitud,
        double longitud,
        int radioMetros,
        CancellationToken ct = default)
    {
        ValidarUbicacion(latitud, longitud);
        ValidarRadio(radioMetros);

        var cercanos = await _busqueda.BuscarCercanosAsync(
            latitud, longitud, radioMetros, DateTime.UtcNow, ct);

        return new ResultadoBusqueda(latitud, longitud, radioMetros, cercanos);
    }

    /// <summary>
    /// Búsqueda a partir de una dirección escrita: el texto se geocodifica y
    /// el resto del flujo es idéntico al de la búsqueda por coordenadas.
    /// </summary>
    public async Task<ResultadoBusqueda> BuscarPorDireccionAsync(
        string direccion,
        int radioMetros,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(direccion);
        ValidarRadio(radioMetros);

        var centro = await _geocodificador.GeocodificarAsync(direccion.Trim(), ct)
            ?? throw new DireccionNoEncontradaException(
                $"No se pudo ubicar la dirección «{direccion.Trim()}».");

        return await BuscarPorUbicacionAsync(centro.Latitud, centro.Longitud, radioMetros, ct);
    }

    /// <summary>
    /// Búsqueda híbrida (D-21.17): primero por nombre o dirección, sin filtro
    /// de radio; si no hay coincidencias, el mismo texto se resuelve como
    /// dirección y se busca por proximidad con <paramref name="radioMetros"/>.
    /// </summary>
    public async Task<ResultadoBusqueda> BuscarPorTextoAsync(
        string texto,
        double latitud,
        double longitud,
        int radioMetros,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        ValidarUbicacion(latitud, longitud);
        ValidarRadio(radioMetros);

        var termino = texto.Trim();
        var porNombre = await _busqueda.BuscarPorTextoAsync(
            latitud, longitud, termino, LimitePorTexto, DateTime.UtcNow, ct);

        if (porNombre.Count > 0)
        {
            return new ResultadoBusqueda(
                latitud, longitud, radioMetros, porNombre, Modo: "nombre");
        }

        var porDireccion = await BuscarPorDireccionAsync(termino, radioMetros, ct);
        return porDireccion with { Modo = "direccion" };
    }

    private static void ValidarUbicacion(double latitud, double longitud)
    {
        if (double.IsNaN(latitud) || latitud is < -90 or > 90)
        {
            throw new UbicacionInvalidaException("La latitud debe estar entre -90 y 90.");
        }

        if (double.IsNaN(longitud) || longitud is < -180 or > 180)
        {
            throw new UbicacionInvalidaException("La longitud debe estar entre -180 y 180.");
        }
    }

    private static void ValidarRadio(int radioMetros)
    {
        if (radioMetros is < RadioMinimoMetros or > RadioMaximoMetros)
        {
            throw new RadioInvalidoException(
                $"El radio debe estar entre {RadioMinimoMetros} y {RadioMaximoMetros} metros.");
        }
    }
}
