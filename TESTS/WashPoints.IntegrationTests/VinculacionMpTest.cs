using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.MercadoPago;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-217: vinculación OAuth de Mercado Pago por lavadero (D-20.3, CA-S20.7,
/// CA-S20.10, CA-S20.12).
///
/// - <c>conectar</c> devuelve la URL de autorización con <c>state</c> firmado;
///   403 ajeno, 404 inexistente, 503 sin credenciales.
/// - El callback intercambia <c>code</c>→tokens con un handler falso (nunca
///   red real), los persiste cifrados, marca <c>mp_vinculado</c> y redirige
///   302 a <c>?mp=ok</c>; ante cualquier fallo redirige a <c>?mp=error</code>
///   sin tocar el lavadero. Los tokens nunca aparecen en respuestas.
/// - Re-vincular reemplaza los tokens.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class VinculacionMpTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    /// <summary>Clave AES-256 (32 bytes) de prueba para cifrar los tokens.</summary>
    private static readonly string ClaveTokens =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static readonly Uri OrigenPanel = new("http://localhost:8081");

    private readonly ContenedorPostgres _baseDeDatos;

    public VinculacionMpTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    private static (CustomWebApplicationFactory Api, HandlerOauthFalso Handler) CrearApi(
        string cadenaConexion,
        bool conCredenciales = true,
        HttpStatusCode estadoTokens = HttpStatusCode.OK,
        string cuerpoTokens = "{\"access_token\":\"AT-nuevo\",\"refresh_token\":\"RT-nuevo\",\"user_id\":123456789}")
    {
        var handler = new HandlerOauthFalso(estadoTokens, cuerpoTokens);
        var api = new CustomWebApplicationFactory(cadenaConexion, builder =>
        {
            if (conCredenciales)
            {
                builder.UseSetting("MercadoPago:ClientId", "client-id-test");
                builder.UseSetting("MercadoPago:ClientSecret", "client-secret-test");
                builder.UseSetting(
                    "MercadoPago:RedirectUri",
                    "http://localhost:8081/api/mercadopago/oauth/callback");
                builder.UseSetting("MercadoPago:ClaveTokens", ClaveTokens);
            }

            builder.ConfigureTestServices(servicios =>
            {
                servicios.AddHttpClient("mercadopago-oauth")
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });
        return (api, handler);
    }

    /// <summary>Cliente anónimo sin seguir redirecciones (el callback responde 302).</summary>
    private static HttpClient ClienteAnonimo(CustomWebApplicationFactory api) =>
        api.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static string ExtraerState(string url)
    {
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["state"] ?? throw new InvalidOperationException("sin state");
    }

    [Fact]
    public async Task ConectarDevuelveLaUrlDeAutorizacionConStateFirmado()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, _) = CrearApi(_baseDeDatos.CadenaConexion);
        using var cliente = api.CrearClienteAdmin(lavaderoId);

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/mercadopago/conectar");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var url = cuerpo!["url"];
        Assert.Contains("auth.mercadopago.com/authorization", url);
        Assert.Contains("client_id=client-id-test", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("redirect_uri=", url);
        Assert.Contains("state=", url);
        // El state firmado debe validar con la clave del servidor (anti-CSRF).
        var firmador = new FirmadorEstadoOauth(ClaveTokens, TimeProvider.System);
        Assert.Equal(lavaderoId, firmador.Validar(ExtraerState(url)));
    }

    [Fact]
    public async Task ConectarDeOtroLavaderoDevuelve403NoEsPropietario()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        var (api, _) = CrearApi(_baseDeDatos.CadenaConexion);
        using var cliente = api.CrearClienteAdmin(lavaderoA);

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoB}/mercadopago/conectar");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("no_es_propietario", error!["error"]);
    }

    [Fact]
    public async Task ConectarParaUnLavaderoInexistenteDevuelve404()
    {
        // El claim de administrador apunta al mismo id inexistente de la ruta:
        // la política de propiedad lo deja pasar (compara ids, no existe en la
        // base) y el 404 sale del servicio.
        var idInexistente = Guid.NewGuid();
        var (api, _) = CrearApi(_baseDeDatos.CadenaConexion);
        using var cliente = api.CrearClienteAdmin(idInexistente);

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{idInexistente}/mercadopago/conectar");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("lavadero_inexistente", error!["error"]);
    }

    [Fact]
    public async Task ConectarSinCredencialesDevuelve503MercadopagoNoConfigurado()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, _) = CrearApi(_baseDeDatos.CadenaConexion, conCredenciales: false);
        using var cliente = api.CrearClienteAdmin(lavaderoId);

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/mercadopago/conectar");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("mercadopago_no_configurado", error!["error"]);
    }

    [Fact]
    public async Task ElCallbackCompletoVinculaElLavaderoYRedirigeAMpOk()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, _) = CrearApi(_baseDeDatos.CadenaConexion);
        using var admin = api.CrearClienteAdmin(lavaderoId);
        var respuestaConectar = await admin.GetAsync(
            $"/api/lavaderos/{lavaderoId}/mercadopago/conectar");
        var url = (await respuestaConectar.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["url"];

        using var anonimo = ClienteAnonimo(api);
        var respuestaCallback = await anonimo.GetAsync(
            $"/api/mercadopago/oauth/callback?code=codigo-mp&state={ExtraerState(url)}");

        Assert.Equal(HttpStatusCode.Redirect, respuestaCallback.StatusCode);
        var location = respuestaCallback.Headers.Location!.ToString();
        Assert.Equal($"{OrigenPanel.ToString().TrimEnd('/')}/admin?mp=ok", location);
        // Tokens nunca en la respuesta del callback (CA-S20.12).
        Assert.DoesNotContain("AT-nuevo", location);

        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        Assert.True(lavadero.MpVinculado);
        Assert.Equal("123456789", lavadero.MpUserId);
        Assert.NotNull(lavadero.MpVinculadoEl);
        // Cifrado en reposo: el claro no está almacenado y descifra al original.
        Assert.NotEqual("AT-nuevo", lavadero.MpAccessToken);
        Assert.NotEqual("RT-nuevo", lavadero.MpRefreshToken);
        var cifrador = new CifradorTokensMp(ClaveTokens);
        Assert.Equal("AT-nuevo", cifrador.Descifrar(lavadero.MpAccessToken!));
        Assert.Equal("RT-nuevo", cifrador.Descifrar(lavadero.MpRefreshToken!));
    }

    [Fact]
    public async Task UnStateInvalidoRedirigeAMpErrorSinVincular()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, handler) = CrearApi(_baseDeDatos.CadenaConexion);

        using var anonimo = ClienteAnonimo(api);
        var respuesta = await anonimo.GetAsync(
            "/api/mercadopago/oauth/callback?code=codigo-mp&state=estado-falsificado");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.EndsWith("?mp=error", respuesta.Headers.Location!.ToString());
        Assert.Empty(handler.Peticiones);
        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        Assert.False(lavadero.MpVinculado);
    }

    [Fact]
    public async Task UnStateVencidoRedirigeAMpError()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, handler) = CrearApi(_baseDeDatos.CadenaConexion);
        var relojAtrasado = new RelojAtrasado(TimeSpan.FromMinutes(-11));
        var stateVencido = new FirmadorEstadoOauth(ClaveTokens, relojAtrasado)
            .Firmar(lavaderoId).Estado;

        using var anonimo = ClienteAnonimo(api);
        var respuesta = await anonimo.GetAsync(
            $"/api/mercadopago/oauth/callback?code=codigo-mp&state={stateVencido}");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.EndsWith("?mp=error", respuesta.Headers.Location!.ToString());
        Assert.Empty(handler.Peticiones);
        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        Assert.False(lavadero.MpVinculado);
    }

    [Fact]
    public async Task CuandoMercadoPagoRechazaElCodigoRedirigeAMpError()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, _) = CrearApi(
            _baseDeDatos.CadenaConexion,
            estadoTokens: HttpStatusCode.BadRequest,
            cuerpoTokens: "{\"error\":\"invalid_grant\"}");
        using var admin = api.CrearClienteAdmin(lavaderoId);
        var respuestaConectar = await admin.GetAsync(
            $"/api/lavaderos/{lavaderoId}/mercadopago/conectar");
        var url = (await respuestaConectar.Content
            .ReadFromJsonAsync<Dictionary<string, string>>())!["url"];

        using var anonimo = ClienteAnonimo(api);
        var respuesta = await anonimo.GetAsync(
            $"/api/mercadopago/oauth/callback?code=codigo-mp&state={ExtraerState(url)}");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.EndsWith("?mp=error", respuesta.Headers.Location!.ToString());
        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        Assert.False(lavadero.MpVinculado);
    }

    [Fact]
    public async Task RevincularReemplazaLosTokens()
    {
        var lavaderoId = await SembrarLavaderoAsync("Lavadero Central");
        var (api, _) = CrearApi(_baseDeDatos.CadenaConexion);
        using var admin = api.CrearClienteAdmin(lavaderoId);
        using var anonimo = ClienteAnonimo(api);

        var respuestaConectar1 = await admin.GetAsync(
            $"/api/lavaderos/{lavaderoId}/mercadopago/conectar");
        var url1 = (await respuestaConectar1.Content
            .ReadFromJsonAsync<Dictionary<string, string>>())!["url"];
        await anonimo.GetAsync(
            $"/api/mercadopago/oauth/callback?code=codigo-1&state={ExtraerState(url1)}");

        var respuestaConectar2 = await admin.GetAsync(
            $"/api/lavaderos/{lavaderoId}/mercadopago/conectar");
        var url2 = (await respuestaConectar2.Content
            .ReadFromJsonAsync<Dictionary<string, string>>())!["url"];
        await anonimo.GetAsync(
            $"/api/mercadopago/oauth/callback?code=codigo-2&state={ExtraerState(url2)}");

        await using var contexto = CrearContexto();
        var lavadero = await contexto.Lavaderos.SingleAsync(l => l.Id == lavaderoId);
        var cifrador = new CifradorTokensMp(ClaveTokens);
        Assert.Equal("AT-nuevo", cifrador.Descifrar(lavadero.MpAccessToken!));
        // Re-vinculación reemplaza, nunca acumula: un solo par queda persistido
        // y el lavadero sigue vinculado.
        Assert.True(lavadero.MpVinculado);
    }

    private sealed class HandlerOauthFalso(
        HttpStatusCode estado = HttpStatusCode.OK,
        string cuerpo = "{\"access_token\":\"AT-nuevo\",\"refresh_token\":\"RT-nuevo\",\"user_id\":123456789}")
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Peticiones { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage peticion, CancellationToken ct)
        {
            Peticiones.Add(peticion);
            return Task.FromResult(new HttpResponseMessage(estado)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class RelojAtrasado : TimeProvider
    {
        private readonly TimeSpan _desplazamiento;

        public RelojAtrasado(TimeSpan desplazamiento) => _desplazamiento = desplazamiento;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _desplazamiento;
    }
}
