namespace WashPoints.Domain.Puertos;

/// <summary>
/// Datos que la aplicación entrega a la pasarela para crear una preferencia de
/// pago de la seña (ARCHITECTURE §7.2).
/// </summary>
/// <param name="TurnoId">Turno al que corresponde la seña. Es la referencia externa de la preferencia.</param>
/// <param name="Monto">Importe de la seña, tomado de <c>tipo_lavado.precio_sena</c>.</param>
/// <param name="Descripcion">Descripción visible al cliente en el checkout.</param>
/// <param name="ReferenciaExterna">
/// Clave estable que la pasarela devuelve en el webhook; permite asociar el
/// pago al turno sin confiar en el orden de llegada.
/// </param>
/// <param name="UrlRetorno">
/// URL de la web a la que el checkout devuelve al cliente tras pagar. Es
/// específica del turno (<c>/turnos/{id}/comprobante?origen=mp</c>) para que
/// la app pueda reconciliar el pago al volver. Si es <c>null</c>, el adaptador
/// usa la URL configurada por defecto.
/// </param>
public sealed record PreferenciaPago(
    Guid TurnoId,
    decimal Monto,
    string Descripcion,
    string ReferenciaExterna,
    string? UrlRetorno = null);
