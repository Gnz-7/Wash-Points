using System.Security.Cryptography;
using System.Text;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Valida la firma <c>x-signature</c> de los webhooks IPN de Mercado Pago
/// (SPECS/slice-01 T-16, CA-S1.5).
///
/// Manifiesto: <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>.
/// Algoritmo: HMAC-SHA256 con el secreto del webhook, en hex minúscula.
/// Comparación en tiempo constante (<see cref="CryptographicOperations.FixedTimeEquals"/>)
/// para no filtrar el hash por tiempos. Tolerancia de <c>ts</c> configurable
/// (por defecto 5 min) para acotar la ventana de replay.
///
/// Formato verificado vía Context7 contra /mercadopago/sdk-nodejs
/// (<c>WebhookSignatureValidator.validate</c>): parsea <c>ts=…,v1=…</c>,
/// construye el manifiesto, calcula el HMAC y compara en tiempo constante.
/// </summary>
public sealed class WebhookSignatureValidator
{
    private static readonly TimeSpan ToleranciaPorDefecto = TimeSpan.FromMinutes(5);

    private readonly string _secreto;
    private readonly TimeSpan _tolerancia;
    private readonly TimeProvider _reloj;

    public WebhookSignatureValidator(string secreto, TimeProvider reloj)
        : this(secreto, ToleranciaPorDefecto, reloj)
    {
    }

    public WebhookSignatureValidator(string secreto, TimeSpan tolerancia, TimeProvider reloj)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secreto);
        _secreto = secreto;
        _tolerancia = tolerancia;
        _reloj = reloj;
    }

    /// <summary>
    /// Devuelve <c>true</c> solo si la firma es válida: header bien formado,
    /// hash coincidente en tiempo constante y <c>ts</c> dentro de la tolerancia.
    /// </summary>
    public bool EsValida(string? xSignature, string? xRequestId, string? dataId)
    {
        if (string.IsNullOrWhiteSpace(xSignature) ||
            string.IsNullOrWhiteSpace(xRequestId) ||
            string.IsNullOrWhiteSpace(dataId))
        {
            return false;
        }

        if (!TryParsearHeader(xSignature, out var ts, out var recibido) ||
            !long.TryParse(ts, out var tsNumerico))
        {
            return false;
        }

        var manifiesto = $"id:{dataId};request-id:{xRequestId};ts:{ts};";
        var esperado = CalcularHmac(manifiesto);

        if (!CompararEnTiempoConstante(esperado, recibido))
        {
            return false;
        }

        return DentroDeTolerancia(tsNumerico);
    }

    private string CalcularHmac(string manifiesto)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secreto));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifiesto)))
            .ToLowerInvariant();
    }

    private static bool CompararEnTiempoConstante(string esperado, string recibido)
    {
        var a = Encoding.UTF8.GetBytes(esperado);
        var b = Encoding.UTF8.GetBytes(recibido);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private bool DentroDeTolerancia(long tsSegundos)
    {
        var instanteFirma = DateTimeOffset.FromUnixTimeSeconds(tsSegundos);
        var desvio = (_reloj.GetUtcNow() - instanteFirma).Duration();
        return desvio <= _tolerancia;
    }

    /// <summary>Parsea <c>ts=…,v1=…</c> (orden y espacios variables).</summary>
    private static bool TryParsearHeader(string header, out string ts, out string v1)
    {
        ts = string.Empty;
        v1 = string.Empty;

        foreach (var parte in header.Split(',', StringSplitOptions.TrimEntries))
        {
            var par = parte.Split('=', 2, StringSplitOptions.TrimEntries);
            if (par.Length != 2)
            {
                continue;
            }

            if (par[0].Equals("ts", StringComparison.OrdinalIgnoreCase))
            {
                ts = par[1];
            }
            else if (par[0].Equals("v1", StringComparison.OrdinalIgnoreCase))
            {
                v1 = par[1];
            }
        }

        return ts.Length > 0 && v1.Length > 0;
    }
}
