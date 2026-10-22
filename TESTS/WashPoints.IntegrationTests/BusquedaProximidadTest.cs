using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-73: búsqueda de lavaderos por proximidad (CU-01, US-C07) end-to-end.
///
/// CA-S7.1 — resultados dentro del radio, ordenados por distancia creciente.
/// CA-S7.2 — cada resultado trae nombre, dirección, distancia y ocupación.
/// CA-S7.3 — el modo dirección delega en el geocodificador; con el stub, 503.
/// CA-S7.4 — un radio sin resultados devuelve 200 con lista vacía.
/// CA-S7.6 — solo lavaderos activos y con coordenadas; 401 sin sesión, 403 admin.
/// CA-S7.7 — la ruta literal no es capturada por /api/lavaderos/{id:guid}.
///
/// Las coordenadas se siembran por SQL: el modelo de dominio todavía no las
/// expone (eso es T-74). Ver SPECS/slice-07-busqueda-proximidad.md T-73 y §5.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class BusquedaProximidadTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    /// <summary>Centro de prueba: Obelisco de Buenos Aires.</summary>
    private const double LatCentro = -34.6037;
    private const double LonCentro = -58.3816;

    private readonly ContenedorPostgres _baseDeDatos;

    public BusquedaProximidadTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                foreach (var tipo in new[]
                         {
                             typeof(WashPoints.Api.WorkerReservas),
                             typeof(WashPoints.Api.PublicadorOutboxWorker),
                             typeof(WashPoints.Api.WorkerAgenda),
                         })
                {
                    var descriptor = servicios.SingleOrDefault(d => d.ImplementationType == tipo);
                    if (descriptor is not null)
                    {
                        servicios.Remove(descriptor);
                    }
                }
            });
        });

    // ---------------------------------------------------------------- siembra

    private sealed record Sembrado(Guid LavaderoId, Guid TipoId, IReadOnlyList<Guid> Puestos);

    /// <summary>
    /// Siembra un lavadero con sus puestos y, si se pasan, le fija la ubicación
    /// por SQL (el dominio todavía no tiene columnas para hacerlo, T-74).
    /// </summary>
    private async Task<Sembrado> SembrarLavaderoAsync(
        string nombre,
        double? latitud = null,
        double? longitud = null,
        string? direccion = null,
        bool activo = true,
        int puestos = 1,
        int puestosActivos = 1)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear(nombre);
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        var creados = new List<Puesto>();
        for (var i = 0; i < puestos; i++)
        {
            creados.Add(Puesto.Crear(lavadero.Id, $"Puesto {i + 1}"));
        }

        for (var i = puestosActivos; i < creados.Count; i++)
        {
            creados[i].Desactivar();
        }

        contexto.Add(lavadero);
        contexto.Add(tipo);
        contexto.AddRange(creados);
        await contexto.SaveChangesAsync();

        var asignaciones = new List<string>();
        if (latitud is not null)
        {
            asignaciones.Add($"latitud = {latitud.Value.ToString("R", CultureInfo.InvariantCulture)}");
        }

        if (longitud is not null)
        {
            asignaciones.Add($"longitud = {longitud.Value.ToString("R", CultureInfo.InvariantCulture)}");
        }

        if (direccion is not null)
        {
            asignaciones.Add($"direccion = '{direccion.Replace("'", "''")}'");
        }

        if (!activo)
        {
            asignaciones.Add("activo = false");
        }

        if (asignaciones.Count > 0)
        {
            await _baseDeDatos.EjecutarSqlAsync(
                $"UPDATE lavaderos SET {string.Join(", ", asignaciones)} WHERE id = '{lavadero.Id}';");
        }

        return new Sembrado(lavadero.Id, tipo.Id, creados.Select(p => p.Id).ToArray());
    }

    /// <summary>Deja un turno confirmado ocupando el puesto ahora mismo.</summary>
    private async Task OcuparAhoraAsync(Sembrado sembrado, Guid puestoId)
    {
        await using var contexto = CrearContexto();

        var ahora = DateTime.UtcNow;
        var turno = Turno.Crear(
            sembrado.LavaderoId, puestoId, Guid.NewGuid(), sembrado.TipoId,
            ahora.AddMinutes(-10), ahora.AddMinutes(20), precioSena: 3000m);

        turno.TransicionarA(EstadoTurno.Confirmado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ casos

    [Fact]
    public async Task DevuelveLosLavaderosDentroDelRadioOrdenadosPorDistancia()
    {
        await SembrarLavaderoAsync("Cerca", -34.6040, -58.3820);
        await SembrarLavaderoAsync("Medio", -34.6100, -58.3900);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetFromJsonAsync<RespuestaBusqueda>(Url(LatCentro, LonCentro, 5000));

        Assert.NotNull(respuesta);
        Assert.Equal(2, respuesta!.Resultados.Count);
        Assert.Equal("Cerca", respuesta.Resultados[0].Nombre);
        Assert.Equal("Medio", respuesta.Resultados[1].Nombre);
        Assert.True(respuesta.Resultados[0].DistanciaMetros < respuesta.Resultados[1].DistanciaMetros);
        Assert.True(respuesta.Resultados[0].DistanciaMetros > 0);
    }

    [Fact]
    public async Task DescartaLavaderosFueraDelRadio()
    {
        await SembrarLavaderoAsync("Cerca", -34.6040, -58.3820);
        await SembrarLavaderoAsync("Lejos", -34.7000, -58.5000);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetFromJsonAsync<RespuestaBusqueda>(Url(LatCentro, LonCentro, 2000));

        Assert.NotNull(respuesta);
        var unico = Assert.Single(respuesta!.Resultados);
        Assert.Equal("Cerca", unico.Nombre);
    }

    [Fact]
    public async Task CadaResultadoTraeNombreDireccionDistanciaYOcupacion()
    {
        var sembrado = await SembrarLavaderoAsync(
            "Lavadero Sol", -34.6040, -58.3820,
            direccion: "Av. Rivadavia 1234",
            puestos: 2, puestosActivos: 1);
        await OcuparAhoraAsync(sembrado, sembrado.Puestos[0]);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetFromJsonAsync<RespuestaBusqueda>(Url(LatCentro, LonCentro, 5000));

        Assert.NotNull(respuesta);
        Assert.Equal(5000, respuesta!.RadioMetros);
        Assert.Equal(LatCentro, respuesta.Centro.Latitud, 4);

        var unico = Assert.Single(respuesta.Resultados);
        Assert.Equal(sembrado.LavaderoId, unico.LavaderoId);
        Assert.Equal("Lavadero Sol", unico.Nombre);
        Assert.Equal("Av. Rivadavia 1234", unico.Direccion);
        Assert.True(unico.DistanciaMetros > 0);

        Assert.Equal(2, unico.Ocupacion.PuestosTotales);
        Assert.Equal(1, unico.Ocupacion.PuestosActivos);
        Assert.Equal(1, unico.Ocupacion.OcupadosAhora);
    }

    [Fact]
    public async Task DescartaLavaderosInactivos()
    {
        await SembrarLavaderoAsync("Cerrado", -34.6040, -58.3820, activo: false);
        await SembrarLavaderoAsync("Abierto", -34.6041, -58.3821);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetFromJsonAsync<RespuestaBusqueda>(Url(LatCentro, LonCentro, 5000));

        Assert.NotNull(respuesta);
        var unico = Assert.Single(respuesta!.Resultados);
        Assert.Equal("Abierto", unico.Nombre);
    }

    [Fact]
    public async Task DescartaLavaderosSinCoordenadas()
    {
        await SembrarLavaderoAsync("Sin mapa");
        await SembrarLavaderoAsync("Con mapa", -34.6040, -58.3820);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetFromJsonAsync<RespuestaBusqueda>(Url(LatCentro, LonCentro, 5000));

        Assert.NotNull(respuesta);
        var unico = Assert.Single(respuesta!.Resultados);
        Assert.Equal("Con mapa", unico.Nombre);
    }

    [Fact]
    public async Task UnRadioSinResultadosDevuelveListaVacia()
    {
        await SembrarLavaderoAsync("Cerca", -34.6040, -58.3820);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var http = await cliente.GetAsync(Url(0, 0, 1000));

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var respuesta = await http.Content.ReadFromJsonAsync<RespuestaBusqueda>();
        Assert.NotNull(respuesta);
        Assert.Empty(respuesta!.Resultados);
    }

    [Fact]
    public async Task SinSesionDevuelve401()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteSinToken();

        var http = await cliente.GetAsync(Url(LatCentro, LonCentro, 5000));

        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
    }

    [Fact]
    public async Task UnAdministradorNoPuedeBuscar()
    {
        var sembrado = await SembrarLavaderoAsync("Lavadero Sol", -34.6040, -58.3820);
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(sembrado.LavaderoId);

        var http = await cliente.GetAsync(Url(LatCentro, LonCentro, 5000));

        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    [Fact]
    public async Task UnaUbicacionInvalidaDevuelve400()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var http = await cliente.GetAsync("/api/lavaderos/cercanos?lat=-91&lon=0&radioMetros=1000");

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
        var error = await http.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("ubicacion_invalida", error!.Error);
    }

    [Fact]
    public async Task UnRadioInvalidoDevuelve400()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var http = await cliente.GetAsync(Url(LatCentro, LonCentro, 99));

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
        var error = await http.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("radio_invalido", error!.Error);
    }

    [Fact]
    public async Task LaBusquedaPorDireccionResponde503ConElStub()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var http = await cliente.GetAsync(
            "/api/lavaderos/cercanos?direccion=Av.%20Rivadavia%201234&radioMetros=1000");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, http.StatusCode);
        var error = await http.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("proveedor_mapas_no_configurado", error!.Error);
    }

    [Fact]
    public async Task LaRutaLiteralNoEsCapturadaPorElIdDeLavadero()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var http = await cliente.GetAsync(Url(0, 0, 1000));

        // Si /{id:guid} capturara "cercanos", el binding fallaría con 404.
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var respuesta = await http.Content.ReadFromJsonAsync<RespuestaBusqueda>();
        Assert.NotNull(respuesta);
        Assert.NotNull(respuesta!.Centro);
        Assert.NotNull(respuesta.Resultados);
    }

    // ------------------------------------------------------------------ apoyo

    private static string Url(double latitud, double longitud, int radioMetros) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"/api/lavaderos/cercanos?lat={latitud}&lon={longitud}&radioMetros={radioMetros}");

    private sealed record RespuestaBusqueda(Centro Centro, int RadioMetros, List<LavaderoCercanoDto> Resultados);

    private sealed record Centro(double Latitud, double Longitud);

    private sealed record LavaderoCercanoDto(
        Guid LavaderoId,
        string Nombre,
        string? Direccion,
        double DistanciaMetros,
        OcupacionDto Ocupacion);

    private sealed record OcupacionDto(int PuestosTotales, int PuestosActivos, int OcupadosAhora);

    private sealed record RespuestaError(string Error);
}
