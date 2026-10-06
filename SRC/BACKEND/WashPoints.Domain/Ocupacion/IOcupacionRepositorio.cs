using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Persistencia de turnos. Puerto del módulo Ocupacion (ARCHITECTURE §2.2).
///
/// Todo lo que escribe en la tabla `turnos` pasa por este puerto: es la
/// garantía de RB-2. Si otro módulo necesita crear una ocupación, lo hace a
/// través de <see cref="IOcupacionServicio"/>, nunca insertando directo.
/// </summary>
public interface IOcupacionRepositorio
{
    Task AgregarAsync(Turno turno, CancellationToken ct);
    Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct);
    Task GuardarAsync(CancellationToken ct);
}