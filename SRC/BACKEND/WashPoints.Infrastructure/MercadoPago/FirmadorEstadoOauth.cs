using System.Security.Cryptography;
using System.Text;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Firma y valida el <c>state</c> anti-CSRF del OAuth de Mercado Pago
/// (slice-20, D-20.3).
///
/// El payload es <c>lavaderoId|nonce|expira</c> firmado con HMAC-SHA256; la
/// clave se deriva de <c>MercadoPago:ClaveTokens</c> (SHA-256 de la cadena).
/// El estado vence a los 10 minutos: un callback tardío o falsificado no toca
/// el lavadero.
/// </summary>
public sealed class FirmadorEstadoOauth
{
    public const int VigenciaMinutos = 10;

    private readonly byte[] _claveHmac;
    private readonly TimeProvider _reloj;

    public FirmadorEstadoOauth(string? claveTokens, TimeProvider reloj)
    {
        _claveHmac = SHA256.HashData(Encoding.UTF8.GetBytes(claveTokens ?? string.Empty));
        _reloj = reloj;
    }

    /// <summary>Firma un estado nuevo para el lavadero indicado.</summary>
    public (string Estado, DateTimeOffset Expira) Firmar(Guid lavaderoId)
    {
        var expira = _reloj.GetUtcNow().AddMinutes(VigenciaMinutos);
        var nonce = Guid.NewGuid();
        var payload = $"{lavaderoId}|{nonce}|{expira.ToUnixTimeSeconds()}";
        return ($"{CodificarBase64Url(payload)}.{CodificarBase64Url(Firma(payload))}", expira);
    }

    /// <summary>
    /// Valida firma y expiración; devuelve el lavadero del estado o
    /// <c>null</c> si algo no cierra (firma, formato o vencimiento).
    /// </summary>
    public Guid? Validar(string? estado)
    {
        if (string.IsNullOrWhiteSpace(estado))
        {
            return null;
        }

        var partes = estado.Split('.');
        if (partes.Length != 2)
        {
            return null;
        }

        string payload;
        byte[] firmaRecibida;
        try
        {
            payload = DecodificarBase64Url(partes[0]);
            firmaRecibida = Convert.FromBase64String(
                partes[1].Replace('-', '+').Replace('_', '/').PadRight(
                    partes[1].Length + (4 - partes[1].Length % 4) % 4, '='));
        }
        catch (FormatException)
        {
            return null;
        }

        var firmaEsperada = Firma(payload);
        if (!CryptographicOperations.FixedTimeEquals(firmaRecibida, firmaEsperada))
        {
            return null;
        }

        var campos = payload.Split('|');
        if (campos.Length != 3 || !Guid.TryParse(campos[0], out var lavaderoId))
        {
            return null;
        }

        if (!long.TryParse(campos[2], out var expiraUnix) ||
            DateTimeOffset.FromUnixTimeSeconds(expiraUnix) < _reloj.GetUtcNow())
        {
            return null;
        }

        return lavaderoId;
    }

    private byte[] Firma(string payload)
    {
        using var hmac = new HMACSHA256(_claveHmac);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string CodificarBase64Url(byte[] bytes) =>
        CodificarBase64UrlRaw(Convert.ToBase64String(bytes));

    private static string CodificarBase64Url(string valor) =>
        CodificarBase64UrlRaw(Convert.ToBase64String(Encoding.UTF8.GetBytes(valor)));

    private static string CodificarBase64UrlRaw(string base64) => base64
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string DecodificarBase64Url(string valor)
    {
        var base64 = valor
            .Replace('-', '+')
            .Replace('_', '/')
            .PadRight(valor.Length + (4 - valor.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
