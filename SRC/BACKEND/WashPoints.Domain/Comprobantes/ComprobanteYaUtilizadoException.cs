namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// El comprobante ya fue canjeado (US-A04 CA-4.2). La clave HTTP lo mapea a
/// 409 <c>comprobante_ya_utilizado</c>. Es el resultado perdedor del canje
/// concurrente (CA-S2.6).
/// </summary>
public sealed class ComprobanteYaUtilizadoException : Exception
{
    public ComprobanteYaUtilizadoException(Guid turnoId)
        : base("El comprobante ya fue utilizado.")
    {
        TurnoId = turnoId;
    }

    /// <summary>Turno del comprobante ya canjeado.</summary>
    public Guid TurnoId { get; }
}
