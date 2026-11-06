namespace WashPoints.Domain.Turnos;

/// <summary>
/// Quién pidió la cancelación de un turno (SPECS/slice-14 D-14.5).
///
/// Se persiste en <c>turnos.cancelado_por</c> y sólo tiene valor cuando el
/// turno está en <see cref="EstadoTurno.Cancelado"/>. Distingue la baja pedida
/// por el cliente —la única que puede originar un reembolso— de la baja
/// administrativa, que no lo genera (D-14.9).
/// </summary>
public enum AutorCancelacion
{
    /// <summary>El cliente dueño del turno lo canceló desde «Mis turnos».</summary>
    Cliente,

    /// <summary>El lavadero lo canceló desde la agenda (D-04.3).</summary>
    Administrador,
}
