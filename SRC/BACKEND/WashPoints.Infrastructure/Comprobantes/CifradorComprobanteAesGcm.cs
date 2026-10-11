using System.Security.Cryptography;
using System.Text;
using WashPoints.Domain.Comprobantes;

namespace WashPoints.Infrastructure.Comprobantes;

/// <summary>
/// Cifrador AES-GCM del token (SPECS/slice-02 §3, decisión D1). Formato de la
/// salida en base64: <c>nonce(12) || tag(16) || ciphertext</c>. El nonce se
/// genera al azar en cada operación, de modo que dos cifrados del mismo token
/// difieren (CA-S2.8).
///
/// La clave va por configuración/<c>Comprobantes:ClaveCifrado</c> (32 bytes en
/// base64, AES-256), nunca en el repositorio (AGENTS §3.4).
/// </summary>
public sealed class CifradorComprobanteAesGcm : ICifradorComprobante
{
    private const int TamanoNonce = 12;
    private const int TamanoTag = 16;
    private const int TamanoClave = 32;

    private readonly byte[]? _clave;

    public CifradorComprobanteAesGcm(string? claveBase64)
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
                "Comprobantes:ClaveCifrado no es base64 válido.", nameof(claveBase64), ex);
        }

        if (clave.Length != TamanoClave)
        {
            throw new ArgumentException(
                "Comprobantes:ClaveCifrado debe ser una clave AES-256 (32 bytes en base64).",
                nameof(claveBase64));
        }

        _clave = clave;
    }

    /// <summary>Indica si hay una clave configurada. Si no, la emisión fallará explícitamente.</summary>
    public bool EstaConfigurado => _clave is not null;

    public string Cifrar(string tokenClaro)
    {
        if (string.IsNullOrWhiteSpace(tokenClaro))
        {
            throw new ArgumentException("El token a cifrar está vacío.", nameof(tokenClaro));
        }

        var clave = _clave ?? throw new InvalidOperationException(
            "No hay clave de cifrado configurada (Comprobantes:ClaveCifrado).");

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
            "No hay clave de cifrado configurada (Comprobantes:ClaveCifrado).");

        byte[] entrada;
        try
        {
            entrada = Convert.FromBase64String(tokenCifrado);
        }
        catch (FormatException)
        {
            throw new ComprobanteInvalidoException("El token cifrado no es base64 válido.");
        }

        if (entrada.Length < TamanoNonce + TamanoTag)
        {
            throw new ComprobanteInvalidoException("El token cifrado está truncado.");
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
            throw new ComprobanteInvalidoException(
                "El token cifrado no pudo descifrarse (clave o integridad inválidas).");
        }

        return Encoding.UTF8.GetString(claro);
    }
}
