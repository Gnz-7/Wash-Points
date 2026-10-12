using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Evento de ocupación (SPECS/slice-03 D11): la foto mínima de un cambio de
/// disponibilidad, que viaja por la outbox y se publica por SignalR al grupo
/// del lavadero. Su payload es consistente con `GET /disponibilidad`.
/// </summary>
public sealed record EventoOcupacion(
    Guid TurnoId,
    Guid LavaderoId,
    Guid PuestoId,
    OrigenTurno Origen,
    EstadoTurno Estado,
    DateTime DesdeUtc,
    DateTime HastaUtc);
