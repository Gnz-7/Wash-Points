using System.Net;
using Microsoft.AspNetCore.Hosting;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-220 (SPECS/slice-20, D-20.2 / CA-S20.2): la política CORS «Desarrollo»
/// responde el preflight con cabeceras CORS fuera de Production, y no existe
/// en Production (la web habla con la API por el mismo origen vía proxy Nginx,
/// D-20.9).
///
/// El arranque también aplica las migraciones (D-20.1): la fixture ya migró su
/// base, así que la segunda pasada del host es un no-op idempotente; si el
/// arranque falla, el host no levanta y estos tests explotan.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CorsTest : IClassFixture<ContenedorPostgres>
{
    private const string Origen = "http://localhost:5173";

    private readonly ContenedorPostgres _baseDeDatos;

    public CorsTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    [Fact]
    public async Task EnDevelopmentElPreflightRespondeConCabecerasCors()
    {
        using var api = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion);
        using var cliente = api.CreateClient();

        var preflight = new HttpRequestMessage(
            HttpMethod.Options,
            $"/api/lavaderos/{Guid.NewGuid()}/disponibilidad");
        preflight.Headers.Add("Origin", Origen);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var respuesta = await cliente.SendAsync(preflight);

        Assert.True(
            respuesta.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"El preflight respondió {respuesta.StatusCode}.");
        Assert.True(respuesta.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task EnProductionElPreflightNoTieneCabecerasCors()
    {
        using var api = new CustomWebApplicationFactory(
            _baseDeDatos.CadenaConexion,
            builder => builder.UseEnvironment("Production"));
        using var cliente = api.CreateClient();

        var preflight = new HttpRequestMessage(
            HttpMethod.Options,
            $"/api/lavaderos/{Guid.NewGuid()}/disponibilidad");
        preflight.Headers.Add("Origin", Origen);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");

        var respuesta = await cliente.SendAsync(preflight);

        Assert.False(
            respuesta.Headers.Contains("Access-Control-Allow-Origin"),
            "Production no debe responder cabeceras CORS (D-20.2).");
    }
}
