namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Ciclo de vida del reintegro de la seña (SPECS/slice-14 D-14.7).
///
/// En este slice sólo se emiten <see cref="Pendiente"/> y
/// <see cref="NoAplica"/>: la cancelación registra la deuda pero no habla con
/// la pasarela (D-14.8). <see cref="Procesado"/> y <see cref="Fallido"/> quedan
/// reservados para el slice de pagos, que es quien ejecuta el reintegro.
/// </summary>
public enum EstadoReembolso
{
    /// <summary>Se debe reintegrar: la fila espera que el worker de pagos la procese.</summary>
    Pendiente,

    /// <summary>La pasarela confirmó el reintegro.</summary>
    Procesado,

    /// <summary>La pasarela rechazó el reintegro.</summary>
    Fallido,

    /// <summary>No corresponde reintegro: sin pago, fuera de plazo o porcentaje cero.</summary>
    NoAplica,
}
