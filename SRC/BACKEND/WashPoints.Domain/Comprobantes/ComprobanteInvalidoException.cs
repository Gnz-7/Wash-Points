namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// El token presentado no corresponde a ningún comprobante conocido, está mal
/// formado o pertenece a otro lavadero.
///
/// En CU-08 el rechazo por otro lavadero (CA-S2.10) debe ser indistinguible de
/// un comprobante inexistente: no se revela si el token existe o no.
/// </summary>
public sealed class ComprobanteInvalidoException : Exception
{
    public ComprobanteInvalidoException(string mensaje)
        : base(mensaje)
    {
    }
}
