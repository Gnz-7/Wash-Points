using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de persistencia de turnos.
///
/// Al guardar <c>Turno.Confirmado</c>, el motor evalúa la EXCLUDE
/// <c>turnos_no_solapamiento</c>: si hay otro turno que ya ocupa el rango,
/// PostgreSQL rechaza con SQLSTATE 23P01 (<c>exclusion_violation</c>). Acá se
/// traduce a la excepción de dominio que la capa HTTP mapea a 409.
///
/// Ver ARCHITECTURE §4.2 (mapeo a 409 slot_ya_ocupado).
/// </summary>
public sealed class OcupacionRepositorio : IOcupacionRepositorio
{
    private const string SqlStateExclusionViolation = "23P01";

    private readonly WashPointsDbContext _contexto;

    public OcupacionRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task AgregarAsync(Turno turno, CancellationToken ct) =>
        await _contexto.Turnos.AddAsync(turno, ct);

    public async Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
        await _contexto.Turnos.SingleOrDefaultAsync(t => t.Id == turnoId, ct);

    public async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await _contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException postgres &&
                  postgres.SqlState == SqlStateExclusionViolation)
        {
            // RE-ordenar no ayuda: la restricción se evalúa por fila. El
            // conflicto se eleva al dominio para que la API responda 409.
            throw new TurnoConflictoException(
                $"El puesto tiene otro turno en el mismo rango. (detalle: {postgres.MessageText})");
        }
    }
}