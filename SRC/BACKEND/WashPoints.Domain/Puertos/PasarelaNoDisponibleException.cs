namespace WashPoints.Domain.Puertos;

/// <summary>
/// La pasarela de pagos no está disponible (red, timeout o error del
/// proveedor). Quien la consume debe manejarla como fallo transitorio: el turno
/// no se confirma ni se cancela por esto.
/// </summary>
public sealed class PasarelaNoDisponibleException : Exception
{
    public PasarelaNoDisponibleException(string mensaje) : base(mensaje)
    {
    }

    public PasarelaNoDisponibleException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}
