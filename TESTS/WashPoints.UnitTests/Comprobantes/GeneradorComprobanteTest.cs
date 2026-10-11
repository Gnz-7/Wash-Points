using WashPoints.Domain.Comprobantes;
using WashPoints.Infrastructure.Comprobantes;
using Xunit;

namespace WashPoints.UnitTests.Comprobantes;

/// <summary>
/// Verifica el generador de comprobantes: entropía del token, estabilidad del
/// hash SHA-256 y que QR y código de respaldo decodifiquen al MISMO token
/// canónico (CA-S2.3, CA-S2.8; SPECS/slice-02 §3, T-25).
/// </summary>
public sealed class GeneradorComprobanteTest
{
    private readonly GeneradorComprobante _generador = new();

    [Fact]
    public void GenerarToken_ProduceTokensDistintosYDeAltaEntropia()
    {
        // 256 bits CSPRNG en base64url: 43 caracteres sin relleno.
        var tokens = Enumerable.Range(0, 200).Select(_ => _generador.GenerarToken()).ToArray();

        Assert.Equal(tokens.Length, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.Equal(43, t.Length));
        Assert.All(tokens, t => Assert.Matches("^[A-Za-z0-9_-]+$", t));
    }

    [Fact]
    public void CalcularHash_EsEstableYNoExponeElToken()
    {
        var token = _generador.GenerarToken();

        var primero = _generador.CalcularHash(token);
        var segundo = _generador.CalcularHash(token);

        Assert.Equal(primero, segundo);
        Assert.Equal(64, primero.Length);
        Assert.Matches("^[0-9a-f]{64}$", primero);
        Assert.DoesNotContain(token, primero);
        Assert.NotEqual(token, primero);
    }

    [Fact]
    public void ConstruirPayloadQr_ContieneElToken()
    {
        var token = _generador.GenerarToken();

        Assert.Equal($"washpoints://comprobante/{token}", _generador.ConstruirPayloadQr(token));
    }

    [Fact]
    public void ElCodigoDeRespaldoEsSoloBase32CrockfordAgrupado()
    {
        var token = _generador.GenerarToken();

        var codigo = _generador.ConstruirCodigoRespaldo(token);

        // Crockford excluye I, L, O y U; bloques de 4 separados por guiones.
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4})*$", codigo);
    }

    [Fact]
    public void QrYCodigoDeRespaldo_DecodificanAlMismoTokenCanonico()
    {
        // CA-S2.3 / US-C08 CA-8.3 / US-A02 CA-2.5.
        var token = _generador.GenerarToken();

        var desdeQr = _generador.ExtraerTokenDePayloadQr(_generador.ConstruirPayloadQr(token));
        var desdeCodigo = _generador.DecodificarCodigoRespaldo(_generador.ConstruirCodigoRespaldo(token));

        Assert.Equal(token, desdeQr);
        Assert.Equal(token, desdeCodigo);
        Assert.Equal(desdeQr, desdeCodigo);
    }

    [Fact]
    public void ElCodigoDeRespaldo_ToleraMinusculasYGuiones()
    {
        var token = _generador.GenerarToken();
        var codigo = _generador.ConstruirCodigoRespaldo(token);

        var transcrito = codigo.Replace("-", "").ToLowerInvariant();

        Assert.Equal(token, _generador.DecodificarCodigoRespaldo(transcrito));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-qr")]
    [InlineData("https://otro.example/comprobante/abc")]
    public void UnPayloadQrDesconocido_LanzaInvalido(string payload)
    {
        Assert.Throws<ComprobanteInvalidoException>(() => _generador.ExtraerTokenDePayloadQr(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!!")]
    [InlineData("ABCDU123")]
    public void UnCodigoDeRespaldoInvalido_LanzaInvalido(string codigo)
    {
        Assert.Throws<ComprobanteInvalidoException>(() => _generador.DecodificarCodigoRespaldo(codigo));
    }
}
