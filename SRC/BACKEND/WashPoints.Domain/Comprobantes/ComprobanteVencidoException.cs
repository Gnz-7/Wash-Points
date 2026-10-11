namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// El comprobante venció antes de ser canjeado (US-A04 CA-4.3). La clave HTTP
/// lo mapea a <c>comprobante_vencido</c>.
/// </summary>
public sealed class ComprobanteVencidoException : Exception
{
    public ComprobanteVencidoException(Guid turnoId, DateTime expiraEn)
        : base("El comprobante está vencido.")
    {
        TurnoId = turnoId;
        ExpiraEn = expiraEn;
    }

    /// <summary>Turno del comprobante vencido.</summary>
    public Guid TurnoId { get; }

    /// <summary>Instante de vencimiento (UTC) que ya pasó.</summary>
    public DateTime ExpiraEn { get; }
}
