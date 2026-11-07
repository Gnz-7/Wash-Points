namespace WashPoints.Domain.Puertos;

/// <summary>
/// Puerto de la pasarela de pagos (ARCHITECTURE §7.1, §7.2).
///
/// El módulo de reserva jamás conoce a Mercado Pago: habla con esta interfaz.
/// La confirmación de un turno depende del resultado que devuelve la pasarela,
/// nunca de una decisión propia (RB-1).
/// </summary>
public interface IPasarelaPagos
{
    /// <summary>
    /// Crea una preferencia de pago para el turno y devuelve su identificador
    /// externo junto con la URL de checkout. El prefijo de idempotencia se
    /// deriva del <c>turnoId</c> para que un reintento del cliente no genere
    /// una preferencia nueva.
    /// </summary>
    Task<PreferenciaCreada> CrearPreferenciaAsync(PreferenciaPago preferencia, CancellationToken ct);

    /// <summary>
    /// Consulta el estado del pago a partir del <c>payment_id</c> que entrega
    /// el webhook (SPECS/slice-15 D-15.2). Un identificador que no sea un
    /// payment id de Mercado Pago devuelve <c>Pendiente</c>, nunca un pago
    /// inventado.
    /// </summary>
    Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct);

    /// <summary>
    /// Busca el pago de un turno por su <c>external_reference</c> (el
    /// <c>turnoId</c>). Es la vía del respaldo del worker cuando el turno
    /// quedó sin notificación: la preferencia no es un payment id y no puede
    /// consultarse como si lo fuera (SPECS/slice-15 D-15.2).
    /// Devuelve <c>Pendiente</c> sin <c>MpPaymentId</c> si todavía no hay pago.
    /// </summary>
    Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(Guid turnoId, CancellationToken ct);

    /// <summary>
    /// Solicita el reintegro de un pago. <paramref name="monto"/> es el importe
    /// de la seña a devolver; si coincide con el total del pago, la pasarela
    /// recibe un reintegro total (SPECS/slice-15 D-15.3).
    ///
    /// <paramref name="claveIdempotencia"/> permite que un reintento del worker
    /// no genere dos reintegros (D-15.5). Se envía como
    /// <c>X-Idempotency-Key</c>.
    /// </summary>
    Task<ResultadoReembolso> ReembolsarAsync(
        string pagoId,
        decimal monto,
        string? claveIdempotencia,
        CancellationToken ct);

    /// <summary>
    /// Consulta un reintegro ya solicitado. Se usa para reconciliar cuando el
    /// proceso cayó después de que la pasarela aceptó pero antes de persistir
    /// el resultado (D-15.5).
    /// </summary>
    Task<ResultadoReembolso> ConsultarReembolsoAsync(
        string pagoId,
        string reembolsoId,
        CancellationToken ct);
}
