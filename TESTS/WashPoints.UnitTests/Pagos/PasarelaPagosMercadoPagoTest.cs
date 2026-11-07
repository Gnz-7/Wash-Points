using System.Net;
using System.Text;
using MercadoPago.Http;
using WashPoints.Domain.Puertos;
using WashPoints.Infrastructure.MercadoPago;
using HttpMethod = System.Net.Http.HttpMethod;

namespace WashPoints.UnitTests.Pagos;

/// <summary>
/// Contrato del adaptador real (slice-15 T-152/T-153/T-154) contra un handler
/// HTTP falso: nunca sale a la red ni necesita credenciales.
/// </summary>
public sealed class PasarelaPagosMercadoPagoTest
{
    private static readonly Guid TurnoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static OpcionesMercadoPago Opciones() => new()
    {
        AccessToken = "TEST-123",
        WebhookSecret = "secreto",
        UrlNotificacion = "https://washpoints.app/api/webhooks/mercadopago",
    };

    private static PasarelaPagosMercadoPago Crear(HandlerFalso handler) =>
        new(Opciones(), new DefaultHttpClient(new HttpClient(handler)));

    private static HttpResponseMessage Json(string cuerpo, HttpStatusCode estado = HttpStatusCode.OK) =>
        new(estado)
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task CrearPreferenciaEnviaTodosLosCamposDelContrato()
    {
        var handler = new HandlerFalso((_, _) => Json(
            """{"id":"pref-1","init_point":"https://checkout.test/pref-1","sandbox_init_point":"https://sandbox.test/pref-1"}"""));
        var pasarela = Crear(handler);

        var creada = await pasarela.CrearPreferenciaAsync(
            new PreferenciaPago(TurnoId, 1500m, "Seña Lavado básico", TurnoId.ToString()),
            CancellationToken.None);

        Assert.Equal("pref-1", creada.PreferenceId);
        Assert.Equal("https://checkout.test/pref-1", creada.InitPoint);

        var llamada = Assert.Single(handler.Llamadas);
        Assert.Equal(HttpMethod.Post, llamada.Metodo);
        Assert.Contains("/checkout/preferences", llamada.Uri);
        Assert.Equal($"turno-{TurnoId}", llamada.Encabezados["X-Idempotency-Key"]);
        Assert.Contains("\"external_reference\"", llamada.Cuerpo);
        Assert.Contains(TurnoId.ToString(), llamada.Cuerpo);
        Assert.Contains("\"notification_url\"", llamada.Cuerpo);
        Assert.Contains("https://washpoints.app/api/webhooks/mercadopago", llamada.Cuerpo);
        Assert.Contains("\"auto_return\":\"approved\"", llamada.Cuerpo);
        Assert.Contains("\"unit_price\":1500.0", llamada.Cuerpo);
        Assert.Contains("\"currency_id\":\"ARS\"", llamada.Cuerpo);
    }

    [Fact]
    public async Task CrearPreferenciaConErrorDeLaApiFallaComoPasarelaNoDisponible()
    {
        var handler = new HandlerFalso((_, _) => Json(
            """{"message":"boom"}""", HttpStatusCode.InternalServerError));
        var pasarela = Crear(handler);

        await Assert.ThrowsAsync<PasarelaNoDisponibleException>(() =>
            pasarela.CrearPreferenciaAsync(
                new PreferenciaPago(TurnoId, 1500m, "Seña", TurnoId.ToString()),
                CancellationToken.None));
    }

    [Fact]
    public async Task VerificarPagoAprobadoDevuelveElTurnoYElPago()
    {
        var handler = new HandlerFalso((_, _) => Json($$"""
            {"id":77,"status":"approved","external_reference":"{{TurnoId}}","transaction_amount":1500.0}
            """));
        var pasarela = Crear(handler);

        var resultado = await pasarela.VerificarPagoAsync("77", CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Aprobado, resultado.Estado);
        Assert.Equal(TurnoId, resultado.TurnoId);
        Assert.Equal("77", resultado.MpPaymentId);
    }

    [Fact]
    public async Task VerificarPagoRechazadoDevuelveRechazado()
    {
        var handler = new HandlerFalso((_, _) => Json($$"""
            {"id":78,"status":"rejected","external_reference":"{{TurnoId}}"}
            """));
        var pasarela = Crear(handler);

        var resultado = await pasarela.VerificarPagoAsync("78", CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Rechazado, resultado.Estado);
        Assert.Equal("78", resultado.MpPaymentId);
    }

    [Fact]
    public async Task VerificarPagoConIdQueNoEsPaymentIdNoLlamaALaApi()
    {
        var handler = new HandlerFalso((_, _) => Json("{}"));
        var pasarela = Crear(handler);

        var resultado = await pasarela.VerificarPagoAsync("pref-1", CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Pendiente, resultado.Estado);
        Assert.Empty(handler.Llamadas);
    }

    [Fact]
    public async Task BuscarPagoPorTurnoConsultaPorExternalReference()
    {
        var handler = new HandlerFalso((_, _) => Json(
            "{\"results\":[{\"id\":77,\"status\":\"approved\",\"external_reference\":\""
            + TurnoId
            + "\"}],\"paging\":{\"total\":1,\"limit\":10,\"offset\":0}}"));
        var pasarela = Crear(handler);

        var resultado = await pasarela.BuscarPagoPorTurnoAsync(TurnoId, CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Aprobado, resultado.Estado);
        var llamada = Assert.Single(handler.Llamadas);
        Assert.Contains("/v1/payments/search", llamada.Uri);
        Assert.Contains($"external_reference={TurnoId}", llamada.Uri);
    }

    [Fact]
    public async Task BuscarPagoSinResultadosDevuelvePendienteSinPago()
    {
        var handler = new HandlerFalso((_, _) => Json(
            """{"results":[],"paging":{"total":0,"limit":10,"offset":0}}"""));
        var pasarela = Crear(handler);

        var resultado = await pasarela.BuscarPagoPorTurnoAsync(TurnoId, CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Pendiente, resultado.Estado);
        Assert.Null(resultado.MpPaymentId);
        Assert.Equal(TurnoId, resultado.TurnoId);
    }

    [Fact]
    public async Task ReembolsoTotalNoEnviaMontoYUsaLaClaveDeIdempotencia()
    {
        var handler = new HandlerFalso((peticion, _) =>
            peticion.Method == HttpMethod.Get
                ? Json("""{"id":77,"status":"approved","transaction_amount":1500.0}""")
                : Json("""{"id":900,"payment_id":77,"amount":1500.0,"status":"approved"}"""));
        var pasarela = Crear(handler);

        var resultado = await pasarela.ReembolsarAsync(
            "77", 1500m, "reembolso-abc", CancellationToken.None);

        Assert.Equal(EstadoReembolsoPasarela.Aprobado, resultado.Estado);
        Assert.Equal("900", resultado.ReembolsoId);

        var post = Assert.Single(handler.Llamadas, l => l.Metodo == HttpMethod.Post);
        Assert.Contains("/v1/payments/77/refunds", post.Uri);
        Assert.DoesNotContain("amount", post.Cuerpo);
        Assert.Equal("reembolso-abc", post.Encabezados["X-Idempotency-Key"]);
    }

    [Fact]
    public async Task ReembolsoParcialEnviaElMonto()
    {
        var handler = new HandlerFalso((peticion, _) =>
            peticion.Method == HttpMethod.Get
                ? Json("""{"id":77,"status":"approved","transaction_amount":1500.0}""")
                : Json("""{"id":901,"payment_id":77,"amount":500.0,"status":"approved"}"""));
        var pasarela = Crear(handler);

        await pasarela.ReembolsarAsync("77", 500m, "reembolso-abc", CancellationToken.None);

        var post = Assert.Single(handler.Llamadas, l => l.Metodo == HttpMethod.Post);
        Assert.Contains("\"amount\":500.0", post.Cuerpo);
    }

    [Fact]
    public async Task ReembolsoMayorAlPagoSeRechazaSinLlamarAlReintegro()
    {
        var handler = new HandlerFalso((_, _) => Json(
            """{"id":77,"status":"approved","transaction_amount":1500.0}"""));
        var pasarela = Crear(handler);

        var resultado = await pasarela.ReembolsarAsync(
            "77", 2000m, "reembolso-abc", CancellationToken.None);

        Assert.Equal(EstadoReembolsoPasarela.Rechazado, resultado.Estado);
        Assert.False(resultado.Reintentable);
        Assert.Single(handler.Llamadas);
    }

    [Fact]
    public async Task ReembolsoConError500EsReintentable()
    {
        var handler = new HandlerFalso((peticion, _) =>
            peticion.Method == HttpMethod.Get
                ? Json("""{"id":77,"status":"approved","transaction_amount":1500.0}""")
                : Json("""{"message":"boom"}""", HttpStatusCode.InternalServerError));
        var pasarela = Crear(handler);

        var resultado = await pasarela.ReembolsarAsync(
            "77", 1500m, "reembolso-abc", CancellationToken.None);

        Assert.Equal(EstadoReembolsoPasarela.Rechazado, resultado.Estado);
        Assert.True(resultado.Reintentable);
    }

    [Fact]
    public async Task ConsultarReembolsoDevuelveElEstadoDeLaPasarela()
    {
        var handler = new HandlerFalso((_, _) => Json(
            """{"id":900,"payment_id":77,"amount":1500.0,"status":"approved"}"""));
        var pasarela = Crear(handler);

        var resultado = await pasarela.ConsultarReembolsoAsync("77", "900", CancellationToken.None);

        Assert.Equal(EstadoReembolsoPasarela.Aprobado, resultado.Estado);
        var llamada = Assert.Single(handler.Llamadas);
        Assert.Contains("/v1/payments/77/refunds/900", llamada.Uri);
    }

    private sealed class HandlerFalso(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Llamada> Llamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var cuerpo = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var encabezados = request.Headers
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

            Llamadas.Add(new Llamada(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                cuerpo,
                encabezados));

            return responder(request, cuerpo);
        }
    }

    private sealed record Llamada(
        HttpMethod Metodo,
        string Uri,
        string Cuerpo,
        Dictionary<string, string> Encabezados);
}
