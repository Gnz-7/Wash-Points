namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Configuración de la integración con Mercado Pago (slice-15, D-15.8).
///
/// Los secretos nunca viven en el repositorio (AGENTS §3.4): se cargan desde
/// variables de entorno con el prefijo <c>Pagos__</c>. En Production la
/// ausencia de cualquiera de las claves impide arrancar; en Development el
/// adaptador se sustituye por <see cref="PasarelaPagosNoConfigurada"/> y la
/// reserva responde 503 <c>pasarela_no_configurada</c>.
/// </summary>
public sealed class OpcionesMercadoPago
{
    public const string Seccion = "Pagos";

    /// <summary>Access token de la cuenta de Mercado Pago (sandbox o producción).</summary>
    public string? AccessToken { get; init; }

    /// <summary>Secreto con el que se valida la firma de los webhooks.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>URL pública a la que Mercado Pago notifica los eventos. Nunca <c>localhost</c>.</summary>
    public string? UrlNotificacion { get; init; }

    /// <summary>URL de retorno del checkout cuando el pago termina.</summary>
    public string UrlRetorno { get; init; } = "https://washpoints.app/pago";

    /// <summary>
    /// Claves obligatorias que faltan. Devuelve los nombres de configuración,
    /// nunca los valores, para que el mensaje de error sea seguro de loguear.
    /// </summary>
    public IReadOnlyList<string> ClavesFaltantes()
    {
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            faltantes.Add($"{Seccion}:AccessToken");
        }

        if (string.IsNullOrWhiteSpace(WebhookSecret))
        {
            faltantes.Add($"{Seccion}:WebhookSecret");
        }

        if (string.IsNullOrWhiteSpace(UrlNotificacion))
        {
            faltantes.Add($"{Seccion}:UrlNotificacion");
        }

        return faltantes;
    }
}
