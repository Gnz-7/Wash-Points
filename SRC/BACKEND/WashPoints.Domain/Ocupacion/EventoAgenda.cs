using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Evento de agenda (SPECS/slice-04 D-04.5): la foto mínima de un cambio de
/// turno para el panel del administrador.
///
/// Viaja por la outbox (tipo <c>agenda_cambiada</c>) y se publica por SignalR al
/// grupo <c>agenda-{id}</c> del lavadero. Es un canal separado del de
/// disponibilidad (<see cref="EventoOcupacion"/>): todo cambio de estado encola
/// uno, aunque no altere el predicado de ocupación (p. ej. el canje).
/// </summary>
public sealed record EventoAgenda(
    Guid TurnoId,
    Guid LavaderoId,
    Guid PuestoId,
    OrigenTurno Origen,
    EstadoTurno Estado,
    DateTime InicioUtc,
    DateTime FinUtc);
