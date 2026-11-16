namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Configuración de la integración con Mercado Pago (slice-15, D-15.8,
/// adaptada en slice-20 D-20.4/D-20.6).
///
/// Los secretos nunca viven en el repositorio (AGENTS §3.4): se cargan desde
/// variables de entorno con el prefijo <c>Pagos__</c>. Slice-20: el token que
/// opera el dinero es el del LAVADERO (OAuth por lavadero, D-20.3), así que
/// <see cref="AccessToken"/> queda <b>obsoleto</b> (sin uso); ninguna
/// credencial bloquea el arranque (D-20.6). El webhook mantiene la firma
/// global <see cref="WebhookSecret"/> y <see cref="UrlNotificacion"/> es
/// documental de la URL configurada en la app de MP.
/// </summary>
public sealed class OpcionesMercadoPago
{
    public const string Seccion = "Pagos";

    /// <summary>
    /// Obsoleto (slice-20): la reserva usa el token del lavadero. Se conserva
    /// la propiedad para no romper configuraciones existentes; sólo la lee
    /// <see cref="ClavesFaltantes"/> (condición del worker de reembolsos).
    /// </summary>
    // Sin [Obsolete]: el proyecto trata los warnings como errores (CS0618) y la
    // propiedad sigue leyéndose en ClavesFaltantes.
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
