using WashPoints.Domain.Puertos;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Adaptador real de <see cref="IPasarelaPagos"/> contra el SDK de Mercado Pago.
///
/// <para>
/// <b>Estado: stub deliberado.</b> La integración real con el SDK .NET de MP
/// está diferida (SPECS/slice-01 §2.3): Context7 no resuelve un library ID
/// propio del SDK .NET y el paquete NuGet vigente debe validarse antes de
/// escribir la integración (AGENTS §5.1). Hasta entonces, este adaptador existe
/// para que el contenedor de DI tenga una implementación en producción; los
/// tests inyectan el doble <c>PasarelaPagosFakeSimulada</c>.
/// </para>
///
/// <para>
/// Trazado <c>preferencia → payment_id</c> (T-12): la implementación real
/// creará la preferencia con <c>PreferenceClient</c> (enviando una clave de
/// idempotencia derivada del <c>turnoId</c>) y, en
/// <see cref="VerificarPagoAsync"/>, buscará el pago por su
/// <c>external_reference</c>/preferencia para resolver el <c>payment_id</c>.
/// </para>
/// </summary>
public sealed class PasarelaPagosMercadoPago : IPasarelaPagos
{
    private const string MensajePendiente =
        "La integración con Mercado Pago está pendiente (SPECS/slice-01 §2.3). " +
        "Validar el SDK .NET vigente con Context7 (AGENTS §5.1) antes de implementarla. " +
        "En pruebas se inyecta PasarelaPagosFakeSimulada.";

    public Task<string> CrearPreferenciaAsync(PreferenciaPago preferencia, CancellationToken ct) =>
        throw new NotSupportedException(MensajePendiente);

    public Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct) =>
        throw new NotSupportedException(MensajePendiente);
}
