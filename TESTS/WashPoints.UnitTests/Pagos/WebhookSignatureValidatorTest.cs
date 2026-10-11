using System.Security.Cryptography;
using System.Text;
using WashPoints.Infrastructure.MercadoPago;
using Xunit;

namespace WashPoints.UnitTests.Pagos;

/// <summary>
/// T-16: validación de la firma del webhook de Mercado Pago.
///
/// El manifiesto es <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>,
/// HMAC-SHA256 en hex, comparación en tiempo constante y tolerancia de
/// <c>ts</c>. Sin firma válida no se procesa nada (CA-S1.4, CA-S1.5).
///
/// Formato verificado vía Context7 contra /mercadopago/sdk-nodejs
/// (WebhookSignatureValidator.validate).
/// </summary>
public sealed class WebhookSignatureValidatorTest
{
    private const string Secreto = "clave-secreta-de-prueba";
    private static readonly DateTimeOffset Ahora =
        new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeProvider Reloj = new RelojFijo(Ahora);

    private static WebhookSignatureValidator Validador(TimeSpan? tolerancia = null) =>
        new(Secreto, tolerancia ?? TimeSpan.FromMinutes(5), Reloj);

    /// <summary>Firma un manifiesto como lo haría Mercado Pago.</summary>
    private static string Firmar(string dataId, string requestId, long ts)
    {
        var manifiesto = $"id:{dataId};request-id:{requestId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secreto));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifiesto))).ToLowerInvariant();
    }

    private static string Header(long ts, string v1) => $"ts={ts},v1={v1}";

    [Fact]
    public void FirmaValidaEsAceptada()
    {
        var ts = Ahora.ToUnixTimeSeconds();
        var v1 = Firmar("123456", "req-abc", ts);

        Assert.True(Validador().EsValida(Header(ts, v1), "req-abc", "123456"));
    }

    [Fact]
    public void SinHeaderDeFirmaEsRechazada()
    {
        Assert.False(Validador().EsValida(null, "req-abc", "123456"));
        Assert.False(Validador().EsValida("", "req-abc", "123456"));
    }

    [Fact]
    public void HeaderMalformadoEsRechazado()
    {
        Assert.False(Validador().EsValida("no-es-un-header-valido", "req-abc", "123456"));
    }

    [Fact]
    public void HashQueNoCoincideEsRechazado()
    {
        var ts = Ahora.ToUnixTimeSeconds();

        Assert.False(Validador().EsValida(Header(ts, "hashfalso"), "req-abc", "123456"));
    }

    [Fact]
    public void FirmaConOtroRequestIdEsRechazada()
    {
        var ts = Ahora.ToUnixTimeSeconds();
        var v1 = Firmar("123456", "req-abc", ts);

        Assert.False(Validador().EsValida(Header(ts, v1), "req-distinto", "123456"));
    }

    [Fact]
    public void FirmaConOtroDataIdEsRechazada()
    {
        var ts = Ahora.ToUnixTimeSeconds();
        var v1 = Firmar("123456", "req-abc", ts);

        Assert.False(Validador().EsValida(Header(ts, v1), "req-abc", "999999"));
    }

    [Fact]
    public void FirmaFueraDeToleranciaEsRechazada()
    {
        // Firma correcta pero de hace 10 minutos, con tolerancia de 5.
        var ts = Ahora.AddMinutes(-10).ToUnixTimeSeconds();
        var v1 = Firmar("123456", "req-abc", ts);

        Assert.False(Validador().EsValida(Header(ts, v1), "req-abc", "123456"));
    }

    [Fact]
    public void FirmaDentroDeLaToleranciaEsAceptada()
    {
        var ts = Ahora.AddMinutes(-4).ToUnixTimeSeconds();
        var v1 = Firmar("123456", "req-abc", ts);

        Assert.True(Validador().EsValida(Header(ts, v1), "req-abc", "123456"));
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;
        public RelojFijo(DateTimeOffset ahora) => _ahora = ahora;
        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
