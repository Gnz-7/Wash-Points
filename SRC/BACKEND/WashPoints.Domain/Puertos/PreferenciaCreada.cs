namespace WashPoints.Domain.Puertos;

/// <summary>
/// Resultado de crear una preferencia de pago (SPECS/slice-09, T-95).
///
/// El identificador es lo que el turno persiste para conciliar el webhook; la
/// URL de checkout es lo que el cliente necesita para pagar la seña y solo
/// tiene sentido en la respuesta del alta (no se persiste).
/// </summary>
/// <param name="PreferenceId">Identificador externo de la preferencia.</param>
/// <param name="InitPoint">
/// URL de checkout de la pasarela. Puede ser nula si la pasarela no la expone.
/// </param>
public sealed record PreferenciaCreada(string PreferenceId, string? InitPoint);
