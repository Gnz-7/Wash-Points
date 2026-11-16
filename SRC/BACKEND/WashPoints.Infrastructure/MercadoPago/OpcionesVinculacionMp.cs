namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Credenciales de la app del operador para el OAuth de vinculación de
/// Mercado Pago (slice-20, D-20.3, sección <c>MercadoPago</c>).
///
/// Son independientes de <see cref="OpcionesMercadoPago"/> (sección
/// <c>Pagos</c>, el token global hoy obsoleto). Ninguna de las cuatro claves
/// es requisito de arranque (D-20.6): sin ellas, <c>conectar</c> responde
/// 503 <c>mercadopago_no_configurado</c>.
/// </summary>
public sealed class OpcionesVinculacionMp
{
    public const string Seccion = "MercadoPago";

    /// <summary>Client id de la app del operador para el OAuth de vinculación.</summary>
    public string? ClientId { get; init; }

    /// <summary>Client secret de la app del operador para el intercambio code→tokens.</summary>
    public string? ClientSecret { get; init; }

    /// <summary>URL del callback OAuth (<c>…/api/mercadopago/oauth/callback</c>).</summary>
    public string? RedirectUri { get; init; }

    /// <summary>
    /// Clave AES-256 (base64) que cifra los tokens de los lavaderos en reposo.
    /// De ella también se deriva la clave HMAC del <c>state</c> anti-CSRF.
    /// </summary>
    public string? ClaveTokens { get; init; }

    /// <summary>
    /// Credenciales que faltan, como nombres de configuración (nunca valores,
    /// AGENTS §3.4).
    /// </summary>
    public IReadOnlyList<string> CredencialesFaltantes()
    {
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            faltantes.Add($"{Seccion}:ClientId");
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            faltantes.Add($"{Seccion}:ClientSecret");
        }

        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            faltantes.Add($"{Seccion}:RedirectUri");
        }

        return faltantes;
    }
}
