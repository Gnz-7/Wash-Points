using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-218: cobro mensual mock de la suscripción (D-20.7, CA-S20.8/S20.9).
///
/// `POST /api/lavaderos/{id}/suscripcion/pagar` activa la suscripción por 30
/// días desde ahora; si sigue vigente responde 409 `suscripcion_activa` y no
/// extiende el vencimiento. 403 ajeno, 404 `lavadero_inexistente`.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class SuscripcionTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public SuscripcionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<Guid> SembrarLavaderoAsync(string nombre)
    {
        await using var contexto = CrearContexto();
        var lavadero = Lavadero.Crear(nombre);
        contexto.Lavaderos.Add(lavadero);
        await contexto.SaveChangesAsync();
        return lavadero.Id;
    }

    private async Task<(HttpResponseMessage Respuesta, Dictionary<string, JsonElement> Cuerpo)> Pagar(
        CustomWebApplicationFactory api, Guid lavaderoId)
    {
        using var cliente = api.CrearClienteAdmin(lavaderoId);
        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/suscripcion/pagar", null);
        var cuerpo = await respuesta.Content
            .ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        return (respuesta, cuerpo!);
    }

    [Fact]
    public async Task ElPrimerCobroActivaLaSuscripcionPorTreintaDias()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);

        var (respuesta, cuerpo) = await Pagar(api, lavaderoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(cuerpo!["suscripcionActiva"].GetBoolean());
        var venceEl = cuerpo["suscripcionVenceEl"].GetDateTimeOffset();
        var ahora = DateTimeOffset.UtcNow;
        Assert.InRange(venceEl, ahora.AddDays(29.9), ahora.AddDays(30.1));

        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        Assert.True(lavadero.SuscripcionActiva);
        Assert.True(lavadero.SuscripcionVigente(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task UnDoblePagoDelMesDevuelve409SuscripcionActiva()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);
        await Pagar(api, lavaderoId);

        var (segunda, cuerpo) = await Pagar(api, lavaderoId);

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("suscripcion_activa", cuerpo!["error"].GetString());
        // No extiende el vencimiento.
        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        var venceEl = lavadero.SuscripcionVenceEl!.Value;
        Assert.InRange(venceEl, DateTimeOffset.UtcNow.AddDays(29.9), DateTimeOffset.UtcNow.AddDays(30.1));
    }

    [Fact]
    public async Task UnAdminAjenoDevuelve403NoEsPropietario()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);
        using var cliente = api.CrearClienteAdmin(lavaderoA);

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoB}/suscripcion/pagar", null);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("no_es_propietario", error!["error"]);
    }

    [Fact]
    public async Task UnLavaderoInexistenteDevuelve404()
    {
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);
        var idInexistente = Guid.NewGuid();
        using var cliente = api.CrearClienteAdmin(idInexistente);

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{idInexistente}/suscripcion/pagar", null);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("lavadero_inexistente", error!["error"]);
    }
}
