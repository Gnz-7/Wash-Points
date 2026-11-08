using System.Net;
using System.Text;
using WashPoints.Domain.Geo;
using WashPoints.Infrastructure.Geo;

namespace WashPoints.UnitTests.Geo;

/// <summary>
/// Contrato del geocodificador MapTiler (T-163): normalización, caché y
/// traducción de fallos. Nunca sale a la red ni usa una key real.
/// </summary>
public sealed class GeocodificadorMapTilerTest
{
    private static OpcionesMapas Opciones() => new()
    {
        ApiKey = "clave-de-prueba",
        UrlBase = "https://api.maptiler.com/geocoding/",
    };

    private static GeocodificadorMapTiler Crear(HandlerFalso handler, OpcionesMapas? opciones = null) =>
        new(opciones ?? Opciones(), new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) });

    private static HttpResponseMessage Json(string cuerpo, HttpStatusCode estado = HttpStatusCode.OK) =>
        new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task DevuelveLasCoordenadasDelPrimerResultado()
    {
        var handler = new HandlerFalso(_ => Json(
            """{"features":[{"geometry":{"coordinates":[-58.3816,-34.6037]}}]}"""));
        var geocodificador = Crear(handler);

        var coordenadas = await geocodificador.GeocodificarAsync("Av. Corrientes 1234");

        Assert.NotNull(coordenadas);
        Assert.Equal(-34.6037, coordenadas!.Latitud, 4);
        Assert.Equal(-58.3816, coordenadas.Longitud, 4);
    }

    [Fact]
    public async Task SinResultadosDevuelveNull()
    {
        var handler = new HandlerFalso(_ => Json("""{"features":[]}"""));
        var geocodificador = Crear(handler);

        Assert.Null(await geocodificador.GeocodificarAsync("direccion inexistente"));
    }

    [Fact]
    public async Task Un404DelProveedorSignificaDireccionNoEncontrada()
    {
        var handler = new HandlerFalso(_ => Json("""{"message":"not found"}""", HttpStatusCode.NotFound));
        var geocodificador = Crear(handler);

        Assert.Null(await geocodificador.GeocodificarAsync("calle que no existe"));
    }

    [Fact]
    public async Task UnErrorDelProveedorSeTraduceANoDisponible()
    {
        var handler = new HandlerFalso(_ => Json("""{"message":"boom"}""", HttpStatusCode.InternalServerError));
        var geocodificador = Crear(handler);

        await Assert.ThrowsAsync<ProveedorMapasNoDisponibleException>(
            () => geocodificador.GeocodificarAsync("Av. Corrientes 1234"));
    }

    [Fact]
    public async Task UnFalloDeRedSeTraduceANoDisponible()
    {
        var handler = new HandlerFalso(_ => throw new HttpRequestException("sin red"));
        var geocodificador = Crear(handler);

        await Assert.ThrowsAsync<ProveedorMapasNoDisponibleException>(
            () => geocodificador.GeocodificarAsync("Av. Corrientes 1234"));
    }

    [Fact]
    public async Task SinApiKeyFallaComoNoConfiguradoSinLlamarAlProveedor()
    {
        var handler = new HandlerFalso(_ => Json("""{"features":[]}"""));
        var geocodificador = Crear(handler, new OpcionesMapas { ApiKey = null });

        await Assert.ThrowsAsync<ProveedorMapasNoConfiguradoException>(
            () => geocodificador.GeocodificarAsync("Av. Corrientes 1234"));
        Assert.Empty(handler.Llamadas);
    }

    [Fact]
    public async Task NormalizaLaDireccionParaReutilizarLaCache()
    {
        var handler = new HandlerFalso(_ => Json(
            """{"features":[{"geometry":{"coordinates":[-58.3816,-34.6037]}}]}"""));
        var geocodificador = Crear(handler);

        await geocodificador.GeocodificarAsync("Av. Corrientes 1234!");
        await geocodificador.GeocodificarAsync("  AV   corrientes   1234  ");

        Assert.Single(handler.Llamadas);
    }

    [Fact]
    public async Task LaUrlIncluyeLaDireccionLaKeyYElIdioma()
    {
        var handler = new HandlerFalso(_ => Json(
            """{"features":[{"geometry":{"coordinates":[-58.3816,-34.6037]}}]}"""));
        var geocodificador = Crear(handler);

        await geocodificador.GeocodificarAsync("Av. Corrientes 1234");

        var uri = Assert.Single(handler.Llamadas);
        Assert.Contains("/geocoding/", uri);
        Assert.Contains("Av.%20Corrientes%201234", uri);
        Assert.Contains("key=clave-de-prueba", uri);
        Assert.Contains("limit=1", uri);
        Assert.Contains("language=es", uri);
    }

    private sealed class HandlerFalso(Func<Uri, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Llamadas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            Llamadas.Add(uri);
            return Task.FromResult(responder(request.RequestUri!));
        }
    }
}
