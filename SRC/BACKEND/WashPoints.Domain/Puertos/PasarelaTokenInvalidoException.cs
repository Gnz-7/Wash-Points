namespace WashPoints.Domain.Puertos;

/// <summary>
/// El token de Mercado Pago usado en la llamada fue rechazado (401). No es un
/// fallo transitorio de la pasarela: significa que el token del lavadero
/// expiró o fue revocado, y el consumidor debe refrescarlo (o desvincular si
/// el refresh falla) antes de reintentar (SPECS/slice-20 D-20.8).
/// </summary>
public sealed class PasarelaTokenInvalidoException : Exception
{
    public PasarelaTokenInvalidoException(string mensaje) : base(mensaje)
    {
    }
}
