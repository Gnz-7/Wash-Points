using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Lectura de la agenda de un lavadero (CU-06, SPECS/slice-04).
///
/// Filtra por rango de fechas (UTC), puesto y estado; ordena por hora de inicio.
/// Es solo lectura: no participa del arbitraje de RB-2 (esa es la EXCLUDE).
/// </summary>
public interface IConsultaAgenda
{
    Task<IReadOnlyList<TurnoAgenda>> ListarAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        Guid? puestoId,
        EstadoTurno? estado,
        CancellationToken ct);
}
