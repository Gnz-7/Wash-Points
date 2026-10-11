namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// Se presentó un comprobante para un turno que aún no está confirmado
/// (US-A02 CA-2.4). No es un canje válido: la atención exige la seña paga
/// (RB-1), por lo que el turno no avanza a <c>en_curso</c>. La clave HTTP lo
/// mapea a 409 <c>pago_pendiente</c>.
/// </summary>
public sealed class ComprobantePagoPendienteException : Exception
{
    public ComprobantePagoPendienteException(Guid turnoId)
        : base("El turno todavía no tiene la seña confirmada.")
    {
        TurnoId = turnoId;
    }

    /// <summary>Turno que sigue pendiente de pago.</summary>
    public Guid TurnoId { get; }
}
