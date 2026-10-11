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

    /// <summary>
    /// Busca el turno por su clave de idempotencia. Es la reconsulta que hace
    /// la reserva cuando el INSERT choca por 23P01 o 23505 (SPECS/slice-01 T-14).
    /// </summary>
    Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Lista los turnos de un lavadero cuyo inicio cae en <c>[desdeUtc, hastaUtc)</c>.
    /// Alimenta la caché offline de comprobantes del día (US-A03 CA-3.1).
    /// </summary>
    Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct);

    Task GuardarAsync(CancellationToken ct);
}
