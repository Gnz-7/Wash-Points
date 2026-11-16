using System.Security.Cryptography;
using WashPoints.Infrastructure.MercadoPago;
using Xunit;

namespace WashPoints.UnitTests.MercadoPago;

/// <summary>
/// T-216 (SPECS/slice-20, D-20.3/D-20.8): cifrador AES-GCM de los tokens de
/// Mercado Pago en reposo. Mismo formato que el cifrador de comprobantes:
/// base64 de <c>nonce(12) || tag(16) || ciphertext</c>, clave AES-256 en
/// base64 desde <c>MercadoPago:ClaveTokens</c>. CA-S20.12: en base de datos
/// los tokens están cifrados y sin clave la vinculación falla explícito.
/// </summary>
public sealed class CifradorTokensMpTest
{
    private static string ClaveAleatoria() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void CifrarYDescifrar_HacenRoundTrip()
    {
        var cifrador = new CifradorTokensMp(ClaveAleatoria());
        var token = "APP_USR-123456789-11223344";

        var cifrado = cifrador.Cifrar(token);

        Assert.NotEqual(token, cifrado);
        Assert.Equal(token, cifrador.Descifrar(cifrado));
    }

    [Fact]
    public void ElCifradoNoContieneElClaroYEsNoDeterminista()
    {
        var cifrador = new CifradorTokensMp(ClaveAleatoria());
        var token = "APP_USR-token-de-alta-entropia";

        var primero = cifrador.Cifrar(token);
        var segundo = cifrador.Cifrar(token);

        Assert.DoesNotContain(token, primero);
        Assert.NotEqual(primero, segundo);
        Assert.Equal(token, cifrador.Descifrar(primero));
        Assert.Equal(token, cifrador.Descifrar(segundo));
    }

    [Fact]
    public void UnTextoCifradoConOtraClaveNoSeDescifra()
    {
        var emisor = new CifradorTokensMp(ClaveAleatoria());
        var ajeno = new CifradorTokensMp(ClaveAleatoria());
        var cifrado = emisor.Cifrar("token");

        Assert.ThrowsAny<Exception>(() => ajeno.Descifrar(cifrado));
    }

    [Fact]
    public void UnaClaveQueNoEsAes256SeRechaza()
    {
        var claveCorta = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Assert.Throws<ArgumentException>(() => new CifradorTokensMp(claveCorta));
    }

    [Fact]
    public void SinClave_NoEstaConfiguradoYCifrarFallaExplicito()
    {
        var cifrador = new CifradorTokensMp(claveBase64: null);

        Assert.False(cifrador.EstaConfigurado);
        Assert.Throws<InvalidOperationException>(() => cifrador.Cifrar("access"));
        Assert.Throws<InvalidOperationException>(() => cifrador.Descifrar("cifrado"));
    }
}
