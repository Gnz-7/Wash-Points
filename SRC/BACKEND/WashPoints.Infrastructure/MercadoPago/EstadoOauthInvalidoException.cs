namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// El <c>state</c> del callback OAuth es inválido: formato basura, firma que no
/// corresponde o expiración vencida. El callback redirige a <c>?mp=error</code>
/// sin tocar el lavadero (slice-20, D-20.3).
/// </summary>
public sealed class EstadoOauthInvalidoException : Exception
{
    public EstadoOauthInvalidoException()
        : base("El state del OAuth de Mercado Pago es inválido o está vencido.")
    {
    }
}
