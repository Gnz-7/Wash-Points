using System.Net;
using System.Text;
using System.Text.Json;
using WashPoints.Infrastructure.MercadoPago;
using Xunit;

namespace WashPoints.UnitTests.MercadoPago;

/// <summary>
/// T-217: el intercambio <c>code</c>→tokens contra
/// <c>POST https://api.mercadopago.com/oauth/token</c> (D-20.3).
///
/// Los tests nunca salen a la red: el <see cref="HttpMessageHandler"/> es falso
/// y captura la petición para verificar el contrato del formulario.
/// </summary>
public sealed class ClienteOauthMpTest
{
    [Fact]
    public async Task IntercambiaElCodigoConElFormularioCompleto()
    {
        var handler = new HandlerFalsoTokens();
        var cliente = new ClienteOauthMp(new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com") });

        var tokens = await cliente.IntercambiarCodigoAsync("codigo-de-prueba", Opciones(), CancellationToken.None);

        Assert.Equal("AT-nuevo", tokens.AccessToken);
        Assert.Equal("RT-nuevo", tokens.RefreshToken);
        Assert.Equal("123456789", tokens.UserId);
        var peticion = handler.UltimaPeticion!;
        Assert.Equal(HttpMethod.Post, peticion.Method);
        Assert.Equal("/oauth/token", peticion.RequestUri!.AbsolutePath);
        var cuerpo = handler.UltimoCuerpo;
        var formulario = System.Web.HttpUtility.ParseQueryString(cuerpo);
        Assert.Equal("authorization_code", formulario["grant_type"]);
        Assert.Equal("client-id-test", formulario["client_id"]);
        Assert.Equal("client-secret-test", formulario["client_secret"]);
        Assert.Equal("http://localhost:8081/api/mercadopago/oauth/callback", formulario["redirect_uri"]);
        Assert.Equal("codigo-de-prueba", formulario["code"]);
    }

    [Fact]
    public async Task UnCodigoRechazadoPorMercadoPagoFallaConExcepcionEspecifica()
    {
        var handler = new HandlerFalsoTokens(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        var cliente = new ClienteOauthMp(new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com") });

        var excepcion = await Assert.ThrowsAnyAsync<IntercambioOauthFallidoException>(
            () => cliente.IntercambiarCodigoAsync("codigo-rechazado", Opciones(), CancellationToken.None));

        Assert.Contains("invalid_grant", excepcion.Message);
    }

    private static OpcionesVinculacionMp Opciones() => new()
    {
        ClientId = "client-id-test",
        ClientSecret = "client-secret-test",
        RedirectUri = "http://localhost:8081/api/mercadopago/oauth/callback",
    };

    private sealed class HandlerFalsoTokens(
        HttpStatusCode estado = HttpStatusCode.OK,
        string cuerpo = "{\"access_token\":\"AT-nuevo\",\"refresh_token\":\"RT-nuevo\",\"user_id\":123456789}")
        : HttpMessageHandler
    {
        public HttpRequestMessage? UltimaPeticion { get; private set; }

        /// <summary>Cuerpo copiado al vuelo: el contenido original se descarta al salir del servicio.</summary>
        public string UltimoCuerpo { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage peticion, CancellationToken ct)
        {
            UltimaPeticion = peticion;
            if (peticion.Content is not null)
            {
                UltimoCuerpo = await peticion.Content.ReadAsStringAsync(ct);
            }

            return new HttpResponseMessage(estado)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            };
        }
    }
}
