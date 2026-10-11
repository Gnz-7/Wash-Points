using System.Security.Cryptography;
using WashPoints.Infrastructure.Comprobantes;
using Xunit;

namespace WashPoints.UnitTests.Comprobantes;

/// <summary>
/// Round-trip AES-GCM del token (CA-S2.8; SPECS/slice-02 §3, T-25). El claro
/// nunca debe aparecer en el cifrado y dos cifrados del mismo token difieren
/// (nonce aleatorio). Regresión de la decisión D1.
/// </summary>
public sealed class CifradorComprobanteTest
{
    private static string ClaveAleatoria() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void CifrarYDescifrar_HacenRoundTrip()
    {
        var cifrador = new CifradorComprobanteAesGcm(ClaveAleatoria());
        var token = "un-token-de-comprobante-123";

        var cifrado = cifrador.Cifrar(token);

        Assert.NotEqual(token, cifrado);
        Assert.Equal(token, cifrador.Descifrar(cifrado));
    }

    [Fact]
    public void ElCifradoNoContieneElClaroYEsNoDeterminista()
    {
        var cifrador = new CifradorComprobanteAesGcm(ClaveAleatoria());
        var token = "token-secreto-de-alta-entropia";

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
        var emisor = new CifradorComprobanteAesGcm(ClaveAleatoria());
        var ajeno = new CifradorComprobanteAesGcm(ClaveAleatoria());
        var cifrado = emisor.Cifrar("token");

        Assert.ThrowsAny<Exception>(() => ajeno.Descifrar(cifrado));
    }

    [Fact]
    public void UnaClaveQueNoEsAes256SeRechaza()
    {
        var claveCorta = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Assert.Throws<ArgumentException>(() => new CifradorComprobanteAesGcm(claveCorta));
    }
}
