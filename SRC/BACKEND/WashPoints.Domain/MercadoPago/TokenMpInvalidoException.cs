namespace WashPoints.Domain.MercadoPago;

/// <summary>
/// El texto cifrado de un token de Mercado Pago no pudo descifrarse: no es
/// base64 válido, está truncado o falló la verificación de integridad
/// (clave distinta o dato alterado). SPECS/slice-20, D-20.8.
/// </summary>
public sealed class TokenMpInvalidoException : Exception
{
    public TokenMpInvalidoException(string mensaje) : base(mensaje)
    {
    }
}
