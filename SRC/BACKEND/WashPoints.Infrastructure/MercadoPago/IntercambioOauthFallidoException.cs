namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Mercado Pago rechazó el intercambio <c>code</c>→tokens del OAuth
/// (código inválido, expirado o ya usado). El callback redirige a
/// <c>?mp=error</c> sin tocar el lavadero (slice-20, D-20.3).
/// </summary>
public sealed class IntercambioOauthFallidoException : Exception
{
    public IntercambioOauthFallidoException(string mensaje) : base(mensaje)
    {
    }
}
