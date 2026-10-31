using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Slice-09 T-95: el delta de backend que el cliente necesita para reservar.
///
/// El catálogo expone puestos y tipos activos con la seña que cobra el
/// servidor (el cliente nunca decide el precio, RB-1). El estado del turno se
/// consulta por id, pero solo para el portador dueño: un turno ajeno responde
/// 404 igual que uno inexistente, para no filtrar existencia (D-06.3).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CatalogoYTurnoTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public CatalogoYTurnoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas()
    {
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE turnos, clientes, puestos, tipos_lavado, lavaderos, usuarios CASCADE;
            """);
    }

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
        });

    private sealed record Escenario(
        Guid LavaderoId,
        Guid PuestoId,
        Guid TipoLavadoId,
        Guid ClienteId,
        Guid TurnoId);

    private async Task<Escenario> Sembrar()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        var turno = Turno.Crear(
            lavadero.Id,
            puesto.Id,
            cliente.Id,
            tipo.Id,
            new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            precioSena: 3000m);

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        await contexto.SaveChangesAsync();

        return new Escenario(lavadero.Id, puesto.Id, tipo.Id, cliente.Id, turno.Id);
    }

    private sealed record RespuestaCatalogo(
        List<PuestoDto> Puestos,
        List<TipoDto> TiposLavado);

    private sealed record PuestoDto(Guid Id, string Nombre);

    private sealed record TipoDto(
        Guid Id,
        string Nombre,
        int DuracionMinutos,
        decimal PrecioTotal,
        decimal PrecioSena);

    private sealed record RespuestaTurno(
        Guid Id,
        string Estado,
        Guid PuestoId,
        Guid LavaderoId,
        decimal PrecioSena,
        string? MpPreferenceId,
        string? InitPoint);

    [Fact]
    public async Task ElCatalogoDevuelvePuestosYTiposConLaSena()
    {
        var escenario = await Sembrar();
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(escenario.ClienteId);

        var catalogo = await cliente.GetFromJsonAsync<RespuestaCatalogo>(
            $"/api/lavaderos/{escenario.LavaderoId}/catalogo");

        Assert.NotNull(catalogo);
        var puesto = Assert.Single(catalogo!.Puestos);
        Assert.Equal(escenario.PuestoId, puesto.Id);
        Assert.Equal("Puesto 1", puesto.Nombre);

        var tipo = Assert.Single(catalogo.TiposLavado);
        Assert.Equal(escenario.TipoLavadoId, tipo.Id);
        Assert.Equal(3000m, tipo.PrecioSena);
    }

    [Fact]
    public async Task ElCatalogoDeUnLavaderoInexistenteDevuelve404()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetAsync($"/api/lavaderos/{Guid.NewGuid()}/catalogo");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElCatalogoExigeSesion()
    {
        var escenario = await Sembrar();
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.GetAsync($"/api/lavaderos/{escenario.LavaderoId}/catalogo");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElClienteConsultaSuPropioTurno()
    {
        var escenario = await Sembrar();
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(escenario.ClienteId);

        var turno = await cliente.GetFromJsonAsync<RespuestaTurno>($"/api/turnos/{escenario.TurnoId}");

        Assert.NotNull(turno);
        Assert.Equal(escenario.TurnoId, turno!.Id);
        Assert.Equal("pendiente_pago", turno.Estado);
        Assert.Equal(3000m, turno.PrecioSena);
        Assert.Equal(escenario.LavaderoId, turno.LavaderoId);
    }

    [Fact]
    public async Task ElTurnoDeOtroClienteResponde404()
    {
        var escenario = await Sembrar();
        using var api = CrearApi();
        using var otro = api.CrearClienteDeCliente();

        var respuesta = await otro.GetAsync($"/api/turnos/{escenario.TurnoId}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElAdministradorDeOtroLavaderoNoVeElTurno()
    {
        var escenario = await Sembrar();
        using var api = CrearApi();
        using var admin = api.CrearClienteAdmin(Guid.NewGuid());

        var respuesta = await admin.GetAsync($"/api/turnos/{escenario.TurnoId}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElAdministradorDelLavaderoSiVeElTurno()
    {
        var escenario = await Sembrar();
        using var api = CrearApi();
        using var admin = api.CrearClienteAdmin(escenario.LavaderoId);

        var turno = await admin.GetFromJsonAsync<RespuestaTurno>($"/api/turnos/{escenario.TurnoId}");

        Assert.NotNull(turno);
        Assert.Equal(escenario.TurnoId, turno!.Id);
    }
}
