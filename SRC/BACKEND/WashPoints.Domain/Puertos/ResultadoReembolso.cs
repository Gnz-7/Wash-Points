namespace WashPoints.Domain.Puertos;

/// <summary>
/// Resultado de pedir o consultar un reintegro a la pasarela (slice-15).
///
/// Igual que <see cref="ResultadoVerificacionPago"/>, se modela como
/// discriminante explícito y no como booleano: cada camino tiene un efecto
/// distinto sobre el estado del <c>Reembolso</c> y sobre la política de
/// reintentos del worker.
/// </summary>
/// <param name="Estado">Camino resuelto por la pasarela.</param>
/// <param name="ReembolsoId">Identificador del reintegro en la pasarela, cuando existe.</param>
/// <param name="Mensaje">Detalle legible para el log; nunca se expone al cliente final.</param>
/// <param name="Reintentable">
/// Indica si un fallo es transitorio y el worker debe reintentar. Un rechazo
/// definitivo (monto inválido, pago no reembolsable) no se reintenta.
/// </param>
public sealed record ResultadoReembolso(
    EstadoReembolsoPasarela Estado,
    string? ReembolsoId,
    string? Mensaje,
    bool Reintentable)
{
    public static ResultadoReembolso Aprobado(string reembolsoId) =>
        new(EstadoReembolsoPasarela.Aprobado, reembolsoId, null, false);

    public static ResultadoReembolso Pendiente(string? reembolsoId = null, string? mensaje = null) =>
        new(EstadoReembolsoPasarela.Pendiente, reembolsoId, mensaje, true);

    public static ResultadoReembolso Rechazado(string mensaje, bool reintentable) =>
        new(EstadoReembolsoPasarela.Rechazado, null, mensaje, reintentable);
}

/// <summary>Los tres caminos que la pasarela puede resolver para un reintegro.</summary>
public enum EstadoReembolsoPasarela
{
    Aprobado,
    Pendiente,
    Rechazado,
}
