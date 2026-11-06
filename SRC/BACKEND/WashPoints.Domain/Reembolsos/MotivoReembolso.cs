namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Razón por la que se otorga o se niega el reintegro de la seña
/// (SPECS/slice-14 D-14.6).
///
/// El motivo se persiste junto al reembolso porque la decisión debe poder
/// auditarse: dos cancelaciones con el mismo monto pueden responder a causas
/// distintas, y el panel de pagos necesita distinguirlas.
/// </summary>
public enum MotivoReembolso
{
    /// <summary>El cliente canceló con la anticipación mínima o más.</summary>
    ClienteEnPlazo,

    /// <summary>El cliente canceló dentro de la ventana en la que la seña no se devuelve.</summary>
    ClienteFueraDePlazo,

    /// <summary>El turno nunca tuvo seña cobrada: seguía pendiente de pago.</summary>
    SinPago,
}
