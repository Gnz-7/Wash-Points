namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// Puerto de emisión del comprobante (SPECS/slice-02 T-26, RB-3).
///
/// Se invoca en la MISMA unidad de trabajo que la transición
/// <c>pendiente_pago → confirmado</c>. La implementación agrega la entidad al
/// contexto compartido, de modo que no puede persistirse un <c>confirmado</c>
/// sin su comprobante.
/// </summary>
public interface IEmisorComprobante
{
    /// <summary>
    /// Emite y agrega al contexto el comprobante del turno confirmado. El
    /// llamador lo persiste en su propio <c>SaveChangesAsync</c>.
    /// </summary>
    /// <param name="turnoId">Turno confirmado al que pertenece el comprobante.</param>
    /// <param name="confirmadoEnUtc">
    /// Instante de confirmación (UTC). El vencimiento es
    /// <c>confirmadoEnUtc + TtlVigenciaHoras</c>.
    /// </param>
    /// <exception cref="ComprobanteNoEmitidoException">
    /// La configuración es insuficiente (p. ej. falta la clave de cifrado).
    /// </exception>
    void Emitir(Guid turnoId, DateTime confirmadoEnUtc);
}
