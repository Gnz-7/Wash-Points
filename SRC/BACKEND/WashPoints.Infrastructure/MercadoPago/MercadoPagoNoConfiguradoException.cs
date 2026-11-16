namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Faltan credenciales del OAuth de vinculación (ClientId/ClientSecret/
/// RedirectUri) o la clave de cifrado de tokens. El endpoint de
/// <c>conectar</c> la traduce a 503 <c>mercadopago_no_configurado</c>
/// (slice-20, D-20.3/D-20.6); nunca bloquea el arranque.
/// </summary>
public sealed class MercadoPagoNoConfiguradoException : Exception
{
    public MercadoPagoNoConfiguradoException()
        : base("La vinculación con Mercado Pago no está configurada.")
    {
    }
}
