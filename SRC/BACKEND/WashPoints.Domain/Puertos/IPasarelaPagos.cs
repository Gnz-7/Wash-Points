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
    /// externo. El prefijo de idempotencia se deriva del <c>turnoId</c> para
    /// que un reintento del cliente no genere una preferencia nueva.
    /// </summary>
    Task<string> CrearPreferenciaAsync(PreferenciaPago preferencia, CancellationToken ct);

    /// <summary>
    /// Consulta el estado del pago asociado a un identificador externo.
    ///
    /// Trazado <c>preferencia → payment_id</c> (SPECS/slice-01 T-12): el turno
    /// persiste el <c>mp_preference_id</c> que devuelve
    /// <see cref="CrearPreferenciaAsync"/>; la implementación real de Mercado
    /// Pago resuelve el <c>payment_id</c> a partir de esa preferencia, porque
    /// el pago lo genera la pasarela recién cuando el cliente paga. El respaldo
    /// del worker (T-21) usa este método para resolver un turno que quedó sin
    /// notificación de webhook.
    /// </summary>
    Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct);
}
