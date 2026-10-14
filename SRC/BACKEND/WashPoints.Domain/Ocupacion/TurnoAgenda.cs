using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Proyección de un turno para la agenda del lavadero (CU-06, SPECS/slice-04
/// T-43/T-46). Lleva los nombres de puesto y tipo de lavado ya resueltos por el
/// join, más el rango en UTC para que el cliente no reinterprete zonas.
/// </summary>
public sealed record TurnoAgenda(
    Guid TurnoId,
    Guid PuestoId,
    string Puesto,
    Guid TipoLavadoId,
    string TipoLavado,
    OrigenTurno Origen,
    EstadoTurno Estado,
    DateTime InicioUtc,
    DateTime FinUtc);
