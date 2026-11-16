using System.Net;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.MercadoPago;
using WashPoints.Infrastructure.MercadoPago;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-219 (SPECS/slice-20, D-20.8): el token de Mercado Pago del lavadero.
///
/// <see cref="VinculacionMpServicio"/> también es la puerta de los tokens que
/// la reserva, el webhook y el worker de reembolsos usan para operar con la
/// cuenta del lavadero:
///
/// - <c>ObtenerTokenVigenteAsync</c> descifra el token en reposo y lo entrega
///   en claro dentro del proceso (nunca en una respuesta HTTP, AGENTS §3.4).
/// - <c>RefrescarTokenAsync</c> renueva el par contra el OAuth de MP y
///   persiste el par nuevo cifrado (re-vinculación automática, D-20.8).
/// - Un lavadero sin vínculo (o con el refresh perdido) lanza
///   <see cref="MercadoPagoNoVinculadoException"/>: la reserva responde 422
///   <c>mercadopago_no_vinculado</c> y el panel ofrece re-vincular.
///
/// Los tests usan un handler HTTP falso: nunca salen a la red (D-20.3).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class VinculacionTokensTest : IClassFixture<ContenedorPostgres>, IAsyncLifetime
{
    private const string ClaveBase64 =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 bytes en cero, sólo para tests.

    private readonly ContenedorPostgres _baseDeDatos;
    private readonly WashPointsDbContext _contexto;
    private readonly CifradorTokensMp _cifrador;
    private readonly HandlerMpFalso _handler;
    private readonly VinculacionMpServicio _servicio;

    public VinculacionTokensTest(ContenedorPostgres baseDeDatos)
    {
        _baseDeDatos = baseDeDatos;
        _contexto = new WashPointsDbContext(
            new DbContextOptionsBuilder<WashPointsDbContext>()
                .UseNpgsql(baseDeDatos.CadenaConexion)
                .Options);
        _cifrador = new CifradorTokensMp(ClaveBase64);
        _handler = new HandlerMpFalso();
        _servicio = new VinculacionMpServicio(
            _contexto,
            new OpcionesVinculacionMp
            {
                ClientId = "cliente-app",
                ClientSecret = "secreto-app",
                RedirectUri = "http://localhost:8081/api/mercadopago/oauth/callback",
                ClaveTokens = ClaveBase64,
            },
            _cifrador,
            new HttpClient(_handler) { BaseAddress = new Uri("https://api.mercadopago.com") },
            TimeProvider.System);

        LimpiarTablas();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _contexto.DisposeAsync();
    }

    private void LimpiarTablas()
    {
        _contexto.Database.ExecuteSqlRaw(
            "TRUNCATE turnos, pagos, reembolsos, outbox, clientes, puestos, tipos_lavado, horarios_atencion, puestos_tipos_lavado, lavaderos CASCADE;");
    }

    private async Task<Lavadero> SembrarLavaderoAsync(string nombre)
    {
        var lavadero = Lavadero.Crear(nombre);
        _contexto.Lavaderos.Add(lavadero);
        await _contexto.SaveChangesAsync();
        return lavadero;
    }

    [Fact]
    public async Task ObtenerTokenVigenteDescifraElTokenDelLavadero()
    {
        var lavadero = await SembrarLavaderoAsync("Lavadero Céntrico");
        lavadero.VincularMercadoPago(
            _cifrador.Cifrar("token-acceso-123"),
            _cifrador.Cifrar("token-refresh-1"),
            "111",
            DateTimeOffset.UtcNow);
        await _contexto.SaveChangesAsync();

        var token = await ((IObtenedorTokensMp)_servicio).ObtenerTokenVigenteAsync(lavadero.Id, CancellationToken.None);

        Assert.Equal("token-acceso-123", token);
    }

    [Fact]
    public async Task UnLavaderoSinVinculacionLanzaMercadoPagoNoVinculado()
    {
        var lavadero = await SembrarLavaderoAsync("Lavadero Suelto");

        await Assert.ThrowsAsync<MercadoPagoNoVinculadoException>(
            () => ((IObtenedorTokensMp)_servicio).ObtenerTokenVigenteAsync(lavadero.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RefrescarTokenRenuevaElParYLoPersisteCifrado()
    {
        var lavadero = await SembrarLavaderoAsync("Lavadero Con Refresh");
        lavadero.VincularMercadoPago(
            _cifrador.Cifrar("token-viejo"),
            _cifrador.Cifrar("refresh-viejo"),
            "111",
            DateTimeOffset.UtcNow);
        await _contexto.SaveChangesAsync();
        _handler.Respuesta = "{\"access_token\": \"token-nuevo\", \"refresh_token\": \"refresh-nuevo\", \"user_id\": \"111\"}";

        var token = await ((IObtenedorTokensMp)_servicio).RefrescarTokenAsync(lavadero.Id, CancellationToken.None);

        Assert.Equal("token-nuevo", token);
        Assert.Equal("grant_type=refresh_token", _handler.FormularioConGrantTypeRefresh);

        var lavaderoEnBase = await _contexto.Lavaderos.SingleAsync(l => l.Id == lavadero.Id);
        Assert.True(lavaderoEnBase.MpVinculado);
        Assert.NotEqual("token-nuevo", lavaderoEnBase.MpAccessToken);
        Assert.Equal("token-nuevo", _cifrador.Descifrar(lavaderoEnBase.MpAccessToken!));
        Assert.Equal("refresh-nuevo", _cifrador.Descifrar(lavaderoEnBase.MpRefreshToken!));
    }

    [Fact]
    public async Task UnRefreshFallidoDesvinculaElLavaderoYLanzaMercadoPagoNoVinculado()
    {
        var lavadero = await SembrarLavaderoAsync("Lavadero Revocado");
        lavadero.VincularMercadoPago(
            _cifrador.Cifrar("token-viejo"),
            _cifrador.Cifrar("refresh-viejo"),
            "111",
            DateTimeOffset.UtcNow);
        await _contexto.SaveChangesAsync();
        _handler.Codigo = HttpStatusCode.BadRequest;

        await Assert.ThrowsAsync<MercadoPagoNoVinculadoException>(
            () => ((IObtenedorTokensMp)_servicio).RefrescarTokenAsync(lavadero.Id, CancellationToken.None));

        var lavaderoEnBase = await _contexto.Lavaderos.SingleAsync(l => l.Id == lavadero.Id);
        Assert.False(lavaderoEnBase.MpVinculado);
        Assert.Null(lavaderoEnBase.MpAccessToken);
        Assert.Null(lavaderoEnBase.MpRefreshToken);
    }

    /// <summary>
    /// Handler falso del OAuth: guarda el formulario si el grant es
    /// refresh_token y responde con el cuerpo/código configurado.
    /// </summary>
    private sealed class HandlerMpFalso : HttpMessageHandler
    {
        public string? Respuesta { get; set; } = "{}";
        public HttpStatusCode Codigo { get; set; } = HttpStatusCode.OK;
        public string? FormularioConGrantTypeRefresh { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            var pares = cuerpo
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

            if (pares.TryGetValue("grant_type", out var grant) && grant == "refresh_token")
            {
                FormularioConGrantTypeRefresh = "grant_type=refresh_token";
            }

            return new HttpResponseMessage(Codigo)
            {
                Content = new StringContent(Respuesta ?? "{}", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
