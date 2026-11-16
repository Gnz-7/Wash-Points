using System.Text;
using System.Text.Json;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Los tokens que Mercado Pago devuelve al intercambiar el código de
/// autorización (slice-20, D-20.3). Viajan en claro dentro del proceso y se
/// cifran antes de persistir; nunca se loguean (AGENTS §3.4).
/// </summary>
public sealed record TokensMp(string AccessToken, string RefreshToken, string UserId);

/// <summary>
/// Cliente HTTP del OAuth de Mercado Pago (slice-20, D-20.3): intercambia el
/// <c>code</c> de la autorización por los tokens del lavadero contra
/// <c>POST https://api.mercadopago.com/oauth/token</c>.
///
/// El <see cref="HttpClient"/> se inyecta (cliente nombrado
/// <c>mercadopago-oauth</c>) para que los tests usen un handler falso y nunca
/// salgan a la red.
/// </summary>
public sealed class ClienteOauthMp
{
    private readonly HttpClient _http;

    public ClienteOauthMp(HttpClient http) => _http = http;

    public async Task<TokensMp> IntercambiarCodigoAsync(
        string code,
        OpcionesVinculacionMp opciones,
        CancellationToken ct)
    {
        using var contenido = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = opciones.ClientId,
            ["client_secret"] = opciones.ClientSecret,
            ["redirect_uri"] = opciones.RedirectUri,
            ["code"] = code,
        });

        using var respuesta = await _http.PostAsync("/oauth/token", contenido, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            throw new IntercambioOauthFallidoException(
                $"Mercado Pago rechazó el intercambio del código (HTTP {(int)respuesta.StatusCode}). {CuerpoDeError(cuerpo)}");
        }

        using var documento = JsonDocument.Parse(cuerpo);
        var raiz = documento.RootElement;
        var accessToken = Cadena(raiz, "access_token");
        var refreshToken = Cadena(raiz, "refresh_token");
        var userId = Cadena(raiz, "user_id");

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new IntercambioOauthFallidoException(
                "Mercado Pago no devolvió los tokens del intercambio OAuth.");
        }

        return new TokensMp(accessToken, refreshToken, userId ?? string.Empty);
    }

    /// <summary>
    /// Renueva el par de tokens con el <c>refresh_token</c> del lavadero
    /// (slice-20, D-20.8): el mismo endpoint de OAuth con
    /// <c>grant_type=refresh_token</c>. Los fallos del intercambio propagan
    /// <see cref="IntercambioOauthFallidoException"/> (el caller decide si
    /// desvincula).
    /// </summary>
    public async Task<TokensMp> IntercambiarRefreshTokenAsync(
        string refreshToken,
        OpcionesVinculacionMp opciones,
        CancellationToken ct)
    {
        using var contenido = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = opciones.ClientId,
            ["client_secret"] = opciones.ClientSecret,
            ["redirect_uri"] = opciones.RedirectUri,
            ["refresh_token"] = refreshToken,
        });

        using var respuesta = await _http.PostAsync("/oauth/token", contenido, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            throw new IntercambioOauthFallidoException(
                $"Mercado Pago rechazó el refresh del token (HTTP {(int)respuesta.StatusCode}). {CuerpoDeError(cuerpo)}");
        }

        using var documento = JsonDocument.Parse(cuerpo);
        var raiz = documento.RootElement;
        var accessToken = Cadena(raiz, "access_token");
        var refreshTokenNuevo = Cadena(raiz, "refresh_token");
        var userId = Cadena(raiz, "user_id");

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshTokenNuevo))
        {
            throw new IntercambioOauthFallidoException(
                "Mercado Pago no devolvió los tokens del refresh.");
        }

        return new TokensMp(accessToken, refreshTokenNuevo, userId ?? string.Empty);
    }

    private static string? Cadena(JsonElement raiz, string nombre)
    {
        if (!raiz.TryGetProperty(nombre, out var valor))
        {
            return null;
        }

        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString(),
            JsonValueKind.Number => valor.GetRawText(),
            _ => valor.GetRawText(),
        };
    }

    /// <summary>
    /// Extrae el mensaje de error de la respuesta sin incluir secretos (sólo el
    /// cuerpo que MP manda, acotado).
    /// </summary>
    private static string CuerpoDeError(string cuerpo)
    {
        var acotado = cuerpo.Length > 200 ? cuerpo[..200] : cuerpo;
        return string.IsNullOrWhiteSpace(acotado) ? string.Empty : acotado.Trim();
    }
}
