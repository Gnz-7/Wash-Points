using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WashPoints.Domain.Geo;

namespace WashPoints.Infrastructure.Geo;

/// <summary>
/// Geocodificación contra MapTiler (D-16.4). Normaliza la dirección para
/// cachear, aplica timeout corto y traduce los fallos del proveedor a las
/// excepciones del dominio: <c>null</c> sólo significa «dirección no hallada».
/// </summary>
public sealed partial class GeocodificadorMapTiler : IGeocodificador
{
    private static readonly TimeSpan VigenciaCache = TimeSpan.FromHours(1);
    private const int MaximoCache = 100;

    private readonly OpcionesMapas _opciones;
    private readonly HttpClient _http;
    private readonly TimeProvider _reloj;
    private readonly Dictionary<string, EntradaCache> _cache = new(StringComparer.Ordinal);
    private readonly object _candado = new();

    public GeocodificadorMapTiler(OpcionesMapas opciones, HttpClient http, TimeProvider? reloj = null)
    {
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<Coordenadas?> GeocodificarAsync(
        string direccion,
        CancellationToken ct = default)
    {
        if (!_opciones.EstaConfigurado)
        {
            throw new ProveedorMapasNoConfiguradoException(
                "La búsqueda por dirección no está disponible: el proveedor de mapas no está configurado.");
        }

        var normalizada = Normalizar(direccion);
        if (normalizada.Length == 0)
        {
            return null;
        }

        if (IntentarDesdeCache(normalizada, out var cacheada))
        {
            return cacheada;
        }

        var coordenadas = await ConsultarAsync(direccion.Trim(), ct);
        GuardarEnCache(normalizada, coordenadas);
        return coordenadas;
    }

    private async Task<Coordenadas?> ConsultarAsync(string direccion, CancellationToken ct)
    {
        var url = $"{_opciones.UrlBase.TrimEnd('/')}/{Uri.EscapeDataString(direccion)}.json"
                  + $"?key={Uri.EscapeDataString(_opciones.ApiKey!)}&limit=1&language=es";

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.GetAsync(url, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProveedorMapasNoDisponibleException(
                "El proveedor de mapas no respondió.", ex);
        }

        using (respuesta)
        {
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                throw new ProveedorMapasNoDisponibleException(
                    $"El proveedor de mapas respondió {(int)respuesta.StatusCode}.");
            }

            try
            {
                await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
                using var documento = await JsonDocument.ParseAsync(flujo, cancellationToken: ct);
                var features = documento.RootElement.GetProperty("features");
                if (features.GetArrayLength() == 0)
                {
                    return null;
                }

                var coordenadas = features[0].GetProperty("geometry").GetProperty("coordinates");
                // GeoJSON: [longitud, latitud].
                return new Coordenadas(
                    coordenadas[1].GetDouble(),
                    coordenadas[0].GetDouble());
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new ProveedorMapasNoDisponibleException(
                    "El proveedor de mapas devolvió una respuesta inesperada.", ex);
            }
        }
    }

    private bool IntentarDesdeCache(string clave, out Coordenadas? coordenadas)
    {
        lock (_candado)
        {
            if (_cache.TryGetValue(clave, out var entrada) &&
                _reloj.GetUtcNow() - entrada.GuardadaEn < VigenciaCache)
            {
                coordenadas = entrada.Coordenadas;
                return true;
            }

            _cache.Remove(clave);
            coordenadas = null;
            return false;
        }
    }

    private void GuardarEnCache(string clave, Coordenadas? coordenadas)
    {
        lock (_candado)
        {
            if (_cache.Count >= MaximoCache)
            {
                _cache.Clear();
            }

            _cache[clave] = new EntradaCache(coordenadas, _reloj.GetUtcNow());
        }
    }

    /// <summary>Minúsculas, sin puntuación y con espacios colapsados (D-16.4).</summary>
    internal static string Normalizar(string direccion) =>
        EspaciosRegex().Replace(
            PuntuacionRegex().Replace(direccion.ToLowerInvariant(), " "),
            " ").Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex PuntuacionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosRegex();

    private sealed record EntradaCache(Coordenadas? Coordenadas, DateTimeOffset GuardadaEn);
}
