using WashPoints.Domain.Puertos;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Pasarela de reemplazo cuando faltan credenciales en Development (D-15.8).
///
/// No simula un cobro: toda operación falla con
/// <see cref="PasarelaNoDisponibleException"/>, que la API traduce a 503
/// <c>pasarela_no_configurada</c>. Es preferible a arrancar con una pasarela
/// que apruebe pagos inexistentes (RB-1).
/// </summary>
public sealed class PasarelaPagosNoConfigurada : IPasarelaPagos
{
    private const string Mensaje =
        "La pasarela de pagos no está configurada. Definí Pagos:AccessToken, " +
        "Pagos:WebhookSecret y Pagos:UrlNotificacion.";

    public Task<PreferenciaCreada> CrearPreferenciaAsync(PreferenciaPago preferencia, CancellationToken ct) =>
        throw new PasarelaNoDisponibleException(Mensaje);

    public Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct) =>
        throw new PasarelaNoDisponibleException(Mensaje);

    public Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
        throw new PasarelaNoDisponibleException(Mensaje);

    public Task<ResultadoReembolso> ReembolsarAsync(
        string pagoId, decimal monto, string? claveIdempotencia, CancellationToken ct) =>
        throw new PasarelaNoDisponibleException(Mensaje);

    public Task<ResultadoReembolso> ConsultarReembolsoAsync(
        string pagoId, string reembolsoId, CancellationToken ct) =>
        throw new PasarelaNoDisponibleException(Mensaje);
}
