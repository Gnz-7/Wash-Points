namespace WashPoints.Domain.Turnos;

/// <summary>
/// Estados de un turno. Los valores coinciden con el enum `turno_estado`
/// de DOCS/ARCHITECTURE.md §4.1.
/// </summary>
public enum EstadoTurno
{
    /// <summary>Creado, sin pagar. No ocupa puesto (RB-1).</summary>
    PendientePago,

    /// <summary>Seña aprobada. Ocupa puesto.</summary>
    Confirmado,

    /// <summary>Cliente atendido, comprobante canjeado. Ocupa puesto.</summary>
    EnCurso,

    /// <summary>Turno cerrado. No ocupa puesto.</summary>
    Finalizado,

    /// <summary>Cancelado por el cliente o por el admin. No ocupa puesto.</summary>
    Cancelado,

    /// <summary>No arribó dentro de la ventana. No ocupa puesto.</summary>
    NoPresentado,

    /// <summary>
    /// Venció el TTL de la seña sin pago confirmado. Terminal: el pago tardío
    /// no lo revive (RB-1). No ocupa ni bloquea. Ver SPECS/slice-01 §3.
    /// </summary>
    Expirado,
}
