using System.Security.Cryptography;
using System.Text;
using WashPoints.Domain.Comprobantes;

namespace WashPoints.Infrastructure.Comprobantes;

/// <summary>
/// Generador de comprobantes (SPECS/slice-02 §3, T-25): token CSPRNG de 256
/// bits, hash SHA-256 y las dos representaciones del MISMO token canónico (QR y
/// código Base32 Crockford).
/// </summary>
public sealed class GeneradorComprobante : IGeneradorComprobante
{
    private const int BytesEntropia = 32;
    private const int TamanoBloque = 4;
    private const string PrefijoQr = IGeneradorComprobante.PrefijoPayloadQr;

    /// <summary>Alfabeto Base32 Crockford: excluye I, L, O y U.</summary>
    private const string AlfabetoCrockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public string GenerarToken()
    {
        Span<byte> bytes = stackalloc byte[BytesEntropia];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    public string CalcularHash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ComprobanteInvalidoException("El token está vacío.");
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string ConstruirPayloadQr(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ComprobanteInvalidoException("El token está vacío.");
        }

        return PrefijoQr + token;
    }

    public string ExtraerTokenDePayloadQr(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            !payload.StartsWith(PrefijoQr, StringComparison.Ordinal))
        {
            throw new ComprobanteInvalidoException(
                "El payload no es un comprobante Wash Points válido.");
        }

        var token = payload[PrefijoQr.Length..];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ComprobanteInvalidoException("El payload no contiene un token.");
        }

        return token;
    }

    public string ConstruirCodigoRespaldo(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ComprobanteInvalidoException("El token está vacío.");
        }

        var bytes = DecodificarBase64Url(token);
        var codigo = CodificarBase32Crockford(bytes);
        return Agrupar(codigo, TamanoBloque);
    }

    public string DecodificarCodigoRespaldo(string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            throw new ComprobanteInvalidoException("El código de respaldo está vacío.");
        }

        // Crockford es insensible a mayúsculas; además I/L se leen como 1 y O
        // como 0. Se descartan los separadores.
        var normalizado = new StringBuilder(codigo.Length);
        foreach (var caracter in codigo)
        {
            if (caracter is '-' or ' ')
            {
                continue;
            }

            var mayuscula = char.ToUpperInvariant(caracter);
            normalizado.Append(mayuscula switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => mayuscula,
            });
        }

        if (normalizado.Length == 0)
        {
            throw new ComprobanteInvalidoException("El código de respaldo está vacío.");
        }

        var bytes = DecodificarBase32Crockford(normalizado.ToString());
        return Base64Url(bytes);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodificarBase64Url(string texto)
    {
        var base64 = texto.Replace('-', '+').Replace('_', '/');
        var relleno = base64.Length % 4;
        base64 = relleno switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        };

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new ComprobanteInvalidoException("El token no es base64url válido.");
        }
    }

    private static string CodificarBase32Crockford(ReadOnlySpan<byte> datos)
    {
        var salida = new StringBuilder((datos.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var dato in datos)
        {
            buffer = (buffer << 8) | dato;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                salida.Append(AlfabetoCrockford[(buffer >> bits) & 31]);
            }

            buffer &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            salida.Append(AlfabetoCrockford[(buffer << (5 - bits)) & 31]);
        }

        return salida.ToString();
    }

    private static byte[] DecodificarBase32Crockford(string texto)
    {
        var salida = new List<byte>(texto.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var caracter in texto)
        {
            var valor = AlfabetoCrockford.IndexOf(char.ToUpperInvariant(caracter));
            if (valor < 0)
            {
                throw new ComprobanteInvalidoException(
                    "El código de respaldo contiene caracteres inválidos.");
            }

            buffer = (buffer << 5) | valor;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                salida.Add((byte)((buffer >> bits) & 0xFF));
            }

            buffer &= (1 << bits) - 1;
        }

        return salida.ToArray();
    }

    private static string Agrupar(string texto, int tamano)
    {
        var salida = new StringBuilder(texto.Length + (texto.Length / tamano));
        for (var i = 0; i < texto.Length; i++)
        {
            if (i > 0 && i % tamano == 0)
            {
                salida.Append('-');
            }

            salida.Append(texto[i]);
        }

        return salida.ToString();
    }
}
