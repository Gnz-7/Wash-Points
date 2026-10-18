using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-54: propiedad del lavadero (CU-10, US-A09 CA-9.3, D-05.7).
///
/// CA-S5.5 — un administrador del lavadero A sobre <c>/api/lavaderos/{B}/...</c>
///           ⇒ 403 no_es_propietario, sin revelar si B existe.
/// CA-S5.6 — dentro del lavadero propio, un sub-recurso (puestoId/turnoId) ajeno
///           sigue siendo 404 (D-04.7 de slice-04).
///
/// Ver SPECS/slice-05-auth-y-permisos.md T-54, §5.2 y D-05.7.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class OwnershipLavaderoTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public OwnershipLavaderoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, usuarios, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion);

    private async Task<Guid> SembrarLavaderoAsync(string nombre)
    {
        await using var contexto = CrearContexto();
        var lavadero = Lavadero.Crear(nombre);
        contexto.Lavaderos.Add(lavadero);
        await contexto.SaveChangesAsync();
        return lavadero.Id;
    }

    private async Task<Guid> SembrarPuestoAsync(Guid lavaderoId, string nombre)
    {
        await using var contexto = CrearContexto();
        var puesto = Puesto.Crear(lavaderoId, nombre);
        contexto.Puestos.Add(puesto);
        await contexto.SaveChangesAsync();
        return puesto.Id;
    }

    private static object CuerpoPuesto(string nombre) => new { nombre };

    [Fact]
    public async Task AdminPropietarioPuedeOperarSuLavadero()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero A");
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(lavaderoId);

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Fact]
    public async Task AdminDeOtroLavaderoDevuelve403NoEsPropietario()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(lavaderoA);

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoB}/puestos", CuerpoPuesto("Puesto Ajeno"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("no_es_propietario", error!.error);
    }

    [Fact]
    public async Task AdminAjenoSobreAgendaDevuelve403()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(lavaderoA);

        var respuesta = await cliente.GetAsync($"/api/lavaderos/{lavaderoB}/agenda");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("no_es_propietario", error!.error);
    }

    [Fact]
    public async Task ElRechazoNoRevelaSiElLavaderoExiste()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        var inexistente = Guid.NewGuid();

        using var api = CrearApi();

        var existente = await CuerpoDeRechazoAsync(api, lavaderoA, lavaderoB);
        var ausente = await CuerpoDeRechazoAsync(api, lavaderoA, inexistente);

        Assert.Equal(existente, ausente);
        Assert.Contains("no_es_propietario", existente);
    }

    private static async Task<string> CuerpoDeRechazoAsync(
        CustomWebApplicationFactory api,
        Guid adminDe,
        Guid destino)
    {
        using var cliente = api.CrearClienteAdmin(adminDe);
        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{destino}/puestos", CuerpoPuesto("Puesto"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        return await respuesta.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task SubRecursoAjenoEnLavaderoPropioDevuelve404()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        var puestoDeB = await SembrarPuestoAsync(lavaderoB, "Puesto B");
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(lavaderoA);

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoA}/puestos/{puestoDeB}/desactivar", null);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("puesto_no_encontrado", error!.error);
    }

    private sealed record RespuestaError(string error);
}
