namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// La emisión del comprobante falló de forma explícita (CU-04). La capa HTTP la
/// mapea a <c>comprobante_no_emitido</c>.
///
/// Es deliberadamente ruidosa: nunca se persiste un comprobante a medias ni un
/// <c>confirmado</c> sin comprobante (RB-3). Un fallo de configuración (p. ej.
/// falta <c>Comprobantes:ClaveCifrado</c>) aborta la confirmación, no la degrada.
/// </summary>
public sealed class ComprobanteNoEmitidoException : Exception
{
    public ComprobanteNoEmitidoException(string mensaje)
        : base(mensaje)
    {
    }

    public ComprobanteNoEmitidoException(string mensaje, Exception inner)
        : base(mensaje, inner)
    {
    }
}
