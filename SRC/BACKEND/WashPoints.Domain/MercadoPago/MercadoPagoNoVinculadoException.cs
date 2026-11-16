namespace WashPoints.Domain.MercadoPago;

/// <summary>
/// El lavadero no tiene una cuenta de Mercado Pago vinculada (o la perdió
/// porque el refresh del token falló). La reserva no puede cobrarse por su
/// cuenta y el cliente debe re-vincular (SPECS/slice-20 D-20.4, D-20.8).
/// </summary>
public sealed class MercadoPagoNoVinculadoException : Exception
{
    public MercadoPagoNoVinculadoException(string mensaje) : base(mensaje)
    {
    }
}
