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
/// T-224: `GET /api/lavaderos/{id}/perfil` (spec §4.4).
///
/// El panel necesita el estado de suscripción y de vinculación de Mercado Pago
/// en una sola lectura: 200 con `suscripcionActiva`, `suscripcionVenceEl` y
/// `mercadopagoVinculado` (nombre exacto del spec); 403 `no_es_propietario`
/// para un admin ajeno y 404 `lavadero_inexistente` para un id inexistente.
/// Los tokens de OAuth NUNCA aparecen en la respuesta.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class PerfilLavaderoTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public PerfilLavaderoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<Guid> SembrarLavaderoAsync(string nombre, bool vinculado = false)
    {
        await using var contexto = CrearContexto();
        var lavadero = Lavadero.Crear(nombre);
        if (vinculado)
        {
            // El dominio recibe los tokens ya cifrados por la vinculación
            // OAuth; estos literales son los valores cifrados de prueba.
            lavadero.VincularMercadoPago(
                "acceso-cifrado", "refresh-cifrado", "999", DateTimeOffset.UtcNow);
        }
        contexto.Lavaderos.Add(lavadero);
        await contexto.SaveChangesAsync();
        return lavadero.Id;
    }

    private async Task<(HttpResponseMessage Respuesta, Dictionary<string, JsonElement> Cuerpo)> Perfil(
        CustomWebApplicationFactory api, Guid lavaderoId)
    {
        using var cliente = api.CrearClienteAdmin(lavaderoId);
        var respuesta = await cliente.GetAsync($"/api/lavaderos/{lavaderoId}/perfil");
        var cuerpo = await respuesta.Content
            .ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        return (respuesta, cuerpo!);
    }

    [Fact]
    public async Task DevuelveLaSuscripcionPendienteYLaVinculacionSinTokens()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Perfil");
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);

        var (respuesta, cuerpo) = await Perfil(api, lavaderoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(cuerpo!["suscripcionActiva"].GetBoolean());
        Assert.True(cuerpo["suscripcionVenceEl"].ValueKind is JsonValueKind.Null);
        Assert.False(cuerpo["mercadopagoVinculado"].GetBoolean());
        // §4.4: los tokens NUNCA aparecen en esta respuesta — sólo las tres claves.
        Assert.Equal(
            new[] { "mercadopagoVinculado", "suscripcionActiva", "suscripcionVenceEl" },
            cuerpo.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ReflejaLaSuscripcionActivaYLaCuentaVinculada()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Perfil", vinculado: true);
        await using (var contexto = CrearContexto())
        {
            var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
            lavadero.ActivarSuscripcion(DateTimeOffset.UtcNow.AddDays(30));
            await contexto.SaveChangesAsync();
        }
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);

        var (respuesta, cuerpo) = await Perfil(api, lavaderoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(cuerpo!["suscripcionActiva"].GetBoolean());
        Assert.NotEqual(
            JsonValueKind.Null, cuerpo["suscripcionVenceEl"].ValueKind);
        Assert.True(cuerpo["mercadopagoVinculado"].GetBoolean());
    }

    [Fact]
    public async Task UnAdminAjenoDevuelve403NoEsPropietario()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);
        using var cliente = api.CrearClienteAdmin(lavaderoA);

        var respuesta = await cliente.GetAsync($"/api/lavaderos/{lavaderoB}/perfil");

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

        var respuesta = await cliente.GetAsync($"/api/lavaderos/{idInexistente}/perfil");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("lavadero_inexistente", error!["error"]);
    }
}
