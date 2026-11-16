using System.Net;
using Microsoft.AspNetCore.Hosting;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-219 / CA-S20.6 (SPECS/slice-20, D-20.6): el despliegue es autónomo.
///
/// Con el entorno Production y SIN ninguna credencial de Mercado Pago
/// (Pagos:AccessToken, Pagos:WebhookSecret, MercadoPago:ClientId/ClientSecret,
/// Comprobantes:ClaveCifrado, Mapas:ApiKey), el host arranca igual y responde
/// HTTP: ningún secreto bloquea la puesta en marcha. Las funciones que los
/// necesitan fallan en caliente (emisión explícita, 503 de la pasarela, OAuth
/// de vinculación no disponible), nunca el arranque.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ArranqueProduccionSinCredencialesTest : IClassFixture<ContenedorPostgres>
{
    private readonly ContenedorPostgres _baseDeDatos;

    public ArranqueProduccionSinCredencialesTest(ContenedorPostgres baseDeDatos) =>
        _baseDeDatos = baseDeDatos;

    [Fact]
    public async Task ElHostArrancaEnProduccionSinNingunaCredencial()
    {
        // La factory ya no define Pagos:*, MercadoPago:*, Comprobantes:* ni
        // Mapas:*: sólo conexión y JWT (los mínimos para servir HTTP).
        using var api = new CustomWebApplicationFactory(
            _baseDeDatos.CadenaConexion,
            builder => builder.UseEnvironment("Production"));
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{Guid.NewGuid()}/disponibilidad");

        // El servidor de Production está vivo y atendiendo sin credenciales:
        // el endpoint anónimo responde 400 porque faltan los parámetros
        // 'desde'/'hasta' (respuesta del pipeline, no del arranque).
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }
}
