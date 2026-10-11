namespace WashPoints.Domain.Puertos;

/// <summary>
/// Resultado de consultar un pago a la pasarela.
///
/// Se modela como discriminante explícito y no como booleano (ARCHITECTURE
/// §7.1) porque cada camino tiene un efecto distinto en la máquina de estados
/// del turno: <c>Aprobado</c> confirma, <c>Rechazado</c> cancela, y
/// <c>Pendiente</c> deja el turno tal como está hasta el TTL.
/// </summary>
/// <param name="Estado">Camino resuelto por la pasarela.</param>
/// <param name="TurnoId">
/// Referencia externa del pago: el turno al que pertenece. La resuelve la
/// implementación (MP devuelve el <c>external_reference</c> de la preferencia).
/// Es lo que permite al webhook mapear un <c>payment_id</c> a su turno sin
/// confiar en el orden de llegada. Puede ser <c>null</c> si no se pudo resolver.
/// </param>
/// <param name="MpPaymentId">
/// Identificador del pago en la pasarela, cuando existe. Es la clave de
/// idempotencia de la tabla <c>pagos</c>.
/// </param>
public sealed record ResultadoVerificacionPago(
    EstadoVerificacionPago Estado,
    Guid? TurnoId,
    string? MpPaymentId)
{
    public static ResultadoVerificacionPago Aprobado(Guid turnoId, string mpPaymentId) =>
        new(EstadoVerificacionPago.Aprobado, turnoId, mpPaymentId);

    public static ResultadoVerificacionPago Pendiente(Guid? turnoId = null, string? mpPaymentId = null) =>
        new(EstadoVerificacionPago.Pendiente, turnoId, mpPaymentId);

    public static ResultadoVerificacionPago Rechazado(Guid? turnoId = null, string? mpPaymentId = null) =>
        new(EstadoVerificacionPago.Rechazado, turnoId, mpPaymentId);
}

/// <summary>Los tres caminos que la pasarela puede resolver (ARCHITECTURE §7.1).</summary>
public enum EstadoVerificacionPago
{
    Aprobado,
    Pendiente,
    Rechazado,
}
