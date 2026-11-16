using System.Security.Cryptography;
using System.Text;
using WashPoints.Domain.MercadoPago;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Cifrador AES-GCM de los tokens de Mercado Pago de cada lavadero
/// (SPECS/slice-20, D-20.3/D-20.8). Mismo formato que el cifrador de
/// comprobantes: salida en base64 de <c>nonce(12) || tag(16) || ciphertext</c>,
/// con nonce aleatorio en cada operación.
///
/// La clave va por configuración/<c>MercadoPago:ClaveTokens</c> (32 bytes en
/// base64, AES-256), nunca en el repositorio (AGENTS §3.4). Sin clave, el
/// cifrador no está configurado y la vinculación falla explícitamente
/// (CA-S20.6).
/// </summary>
public sealed class CifradorTokensMp : ICifradorTokensMp
{
    private const int TamanoNonce = 12;
    private const int TamanoTag = 16;
    private const int TamanoClave = 32;

    private readonly byte[]? _clave;

    public CifradorTokensMp(string? claveBase64)
    {
        if (string.IsNullOrWhiteSpace(claveBase64))
        {
            _clave = null;
            return;
        }

        byte[] clave;
        try
        {
            clave = Convert.FromBase64String(claveBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException(
                "MercadoPago:ClaveTokens no es base64 válido.", nameof(claveBase64), ex);
        }

        if (clave.Length != TamanoClave)
        {
            throw new ArgumentException(
                "MercadoPago:ClaveTokens debe ser una clave AES-256 (32 bytes en base64).",
                nameof(claveBase64));
        }

        _clave = clave;
    }

    /// <summary>Indica si hay una clave configurada. Si no, la vinculación fallará explícitamente.</summary>
    public bool EstaConfigurado => _clave is not null;

    public string Cifrar(string tokenClaro)
    {
        if (string.IsNullOrWhiteSpace(tokenClaro))
        {
            throw new ArgumentException("El token a cifrar está vacío.", nameof(tokenClaro));
        }

        var clave = _clave ?? throw new InvalidOperationException(
            "No hay clave de cifrado configurada (MercadoPago:ClaveTokens).");

        var nonce = new byte[TamanoNonce];
        RandomNumberGenerator.Fill(nonce);

        var claro = Encoding.UTF8.GetBytes(tokenClaro);
        var cipher = new byte[claro.Length];
        var tag = new byte[TamanoTag];

        using (var aes = new AesGcm(clave, TamanoTag))
        {
            aes.Encrypt(nonce, claro, cipher, tag);
        }

        var salida = new byte[TamanoNonce + TamanoTag + cipher.Length];
        Buffer.BlockCopy(nonce, 0, salida, 0, TamanoNonce);
        Buffer.BlockCopy(tag, 0, salida, TamanoNonce, TamanoTag);
        Buffer.BlockCopy(cipher, 0, salida, TamanoNonce + TamanoTag, cipher.Length);
        return Convert.ToBase64String(salida);
    }

    public string Descifrar(string tokenCifrado)
    {
        if (string.IsNullOrWhiteSpace(tokenCifrado))
        {
            throw new ArgumentException("El token cifrado está vacío.", nameof(tokenCifrado));
        }

        var clave = _clave ?? throw new InvalidOperationException(
            "No hay clave de cifrado configurada (MercadoPago:ClaveTokens).");

        byte[] entrada;
        try
        {
            entrada = Convert.FromBase64String(tokenCifrado);
        }
        catch (FormatException)
        {
            throw new TokenMpInvalidoException("El token cifrado no es base64 válido.");
        }

        if (entrada.Length < TamanoNonce + TamanoTag)
        {
            throw new TokenMpInvalidoException("El token cifrado está truncado.");
        }

        var nonce = entrada.AsSpan(0, TamanoNonce);
        var tag = entrada.AsSpan(TamanoNonce, TamanoTag);
        var cipher = entrada.AsSpan(TamanoNonce + TamanoTag);
        var claro = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(clave, TamanoTag);
            aes.Decrypt(nonce, cipher, tag, claro);
        }
        catch (CryptographicException)
        {
            throw new TokenMpInvalidoException(
                "El token cifrado no pudo descifrarse (clave o integridad inválidas).");
        }

        return Encoding.UTF8.GetString(claro);
    }
}
