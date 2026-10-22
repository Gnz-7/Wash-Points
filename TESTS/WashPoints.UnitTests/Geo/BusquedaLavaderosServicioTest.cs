using WashPoints.Application.Geo;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Geo;
using Xunit;

namespace WashPoints.UnitTests.Geo;

/// <summary>
/// Reglas de la búsqueda por proximidad (CU-01 / US-C07). Cubre la validación
/// de parámetros y la delegación en el geocodificador: el servicio no decide
/// distancias ni disponibilidad, solo orquesta (D-08.3, D-08.4).
/// </summary>
public sealed class BusquedaLavaderosServicioTest
{
    private const double LatitudCentro = -34.6037;
    private const double LongitudCentro = -58.3816;

    [Fact]
    public async Task UnaLatitudFueraDeRangoSeRechaza()
    {
        var (servicio, _, _) = CrearServicio();

        await Assert.ThrowsAsync<UbicacionInvalidaException>(
            () => servicio.BuscarPorUbicacionAsync(90.5, LongitudCentro, 5000));
    }

    [Fact]
    public async Task UnaLongitudFueraDeRangoSeRechaza()
    {
        var (servicio, _, _) = CrearServicio();

        await Assert.ThrowsAsync<UbicacionInvalidaException>(
            () => servicio.BuscarPorUbicacionAsync(LatitudCentro, 180.5, 5000));
    }

    [Fact]
    public async Task UnRadioMenorAlMinimoSeRechaza()
    {
        var (servicio, _, _) = CrearServicio();

        await Assert.ThrowsAsync<RadioInvalidoException>(
            () => servicio.BuscarPorUbicacionAsync(LatitudCentro, LongitudCentro, 99));
    }

    [Fact]
    public async Task UnRadioMayorAlMaximoSeRechaza()
    {
        var (servicio, _, _) = CrearServicio();

        await Assert.ThrowsAsync<RadioInvalidoException>(
            () => servicio.BuscarPorUbicacionAsync(LatitudCentro, LongitudCentro, 50001));
    }

    [Fact]
    public async Task ElCentroGeocodificadoSeDelegaALaBusqueda()
    {
        var (servicio, busqueda, geocodificador) = CrearServicio();
        geocodificador.Resultado = new Coordenadas(LatitudCentro, LongitudCentro);
        busqueda.Resultado = new[]
        {
            new LavaderoCercano(Guid.NewGuid(), "Lavadero Uno", "Av. Siempreviva 742", 850.5, 4, 3, 2),
        };

        var resultado = await servicio.BuscarPorDireccionAsync("Av. Siempreviva 742", 5000);

        Assert.Equal("Av. Siempreviva 742", geocodificador.DireccionRecibida);
        Assert.Equal(LatitudCentro, busqueda.LatitudRecibida);
        Assert.Equal(LongitudCentro, busqueda.LongitudRecibida);
        Assert.Equal(5000, busqueda.RadioRecibido);
        Assert.Equal(LatitudCentro, resultado.Latitud);
        Assert.Equal(LongitudCentro, resultado.Longitud);
        Assert.Equal(5000, resultado.RadioMetros);
        Assert.Single(resultado.Resultados);
    }

    [Fact]
    public async Task LaExcepcionDelStubSePropaga()
    {
        var (servicio, _, geocodificador) = CrearServicio();
        geocodificador.Error = new ProveedorMapasNoConfiguradoException(
            "El proveedor de mapas no está configurado.");

        await Assert.ThrowsAsync<ProveedorMapasNoConfiguradoException>(
            () => servicio.BuscarPorDireccionAsync("Av. Corrientes 1234", 5000));
    }

    [Fact]
    public async Task UnaDireccionNoEncontradaSeInforma()
    {
        var (servicio, _, geocodificador) = CrearServicio();
        geocodificador.Resultado = null;

        await Assert.ThrowsAsync<DireccionNoEncontradaException>(
            () => servicio.BuscarPorDireccionAsync("Calle que no existe 9999", 5000));
    }

    private static (BusquedaLavaderosServicio Servicio, BusquedaFake Busqueda, GeocodificadorFake Geocodificador)
        CrearServicio()
    {
        var busqueda = new BusquedaFake();
        var geocodificador = new GeocodificadorFake();
        return (new BusquedaLavaderosServicio(busqueda, geocodificador), busqueda, geocodificador);
    }

    private sealed class BusquedaFake : IBusquedaLavaderos
    {
        public IReadOnlyList<LavaderoCercano> Resultado { get; set; } = Array.Empty<LavaderoCercano>();
        public double? LatitudRecibida { get; private set; }
        public double? LongitudRecibida { get; private set; }
        public int? RadioRecibido { get; private set; }

        public Task<IReadOnlyList<LavaderoCercano>> BuscarCercanosAsync(
            double latitud,
            double longitud,
            int radioMetros,
            DateTime ahoraUtc,
            CancellationToken ct = default)
        {
            LatitudRecibida = latitud;
            LongitudRecibida = longitud;
            RadioRecibido = radioMetros;
            return Task.FromResult(Resultado);
        }
    }

    private sealed class GeocodificadorFake : IGeocodificador
    {
        public Coordenadas? Resultado { get; set; }
        public Exception? Error { get; set; }
        public string? DireccionRecibida { get; private set; }

        public Task<Coordenadas?> GeocodificarAsync(string direccion, CancellationToken ct = default)
        {
            DireccionRecibida = direccion;
            return Error is null
                ? Task.FromResult(Resultado)
                : Task.FromException<Coordenadas?>(Error);
        }
    }
}
