using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de persistencia de turnos.
///
/// Al guardar un turno, el motor evalúa la EXCLUDE
/// <c>turnos_no_solapamiento</c>: si hay otro turno vigente en el rango,
/// PostgreSQL rechaza con SQLSTATE 23P01 (<c>exclusion_violation</c>). El
/// índice único parcial de <c>idempotency_key</c> rechaza un reintento cuya
/// fila original ya salió del predicado con SQLSTATE 23505
/// (<c>unique_violation</c>). Ambas se traducen a excepciones de dominio que la
/// reserva sabe manejar (SPECS/slice-01 T-14).
///
/// Ver ARCHITECTURE §4.2 (mapeo a 409 slot_ya_ocupado).
/// </summary>
public sealed class OcupacionRepositorio : IOcupacionRepositorio
{
    private const string SqlStateExclusionViolation = "23P01";
    private const string SqlStateUniqueViolation = "23505";
    private const string IndiceIdempotencia = "turnos_idempotency_unicos";

    private const string SqlTurnosDelDia = """
        SELECT id, lavadero_id, puesto_id, cliente_id, tipo_lavado_id,
               rango, estado, origen, precio_sena, mp_preference_id,
               expira_en, idempotency_key, creado_en, confirmado_en, cerrado_en
        FROM turnos
        WHERE lavadero_id = {0}
          AND lower(rango) >= @desdeLocal
          AND lower(rango) < @hastaLocal
        ORDER BY lower(rango)
        """;

    private readonly WashPointsDbContext _contexto;

    public OcupacionRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task AgregarAsync(Turno turno, CancellationToken ct) =>
        await _contexto.Turnos.AddAsync(turno, ct);

    public async Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
        await _contexto.Turnos.SingleOrDefaultAsync(t => t.Id == turnoId, ct);

    public async Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        await _contexto.Turnos
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, ct);

    public async Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct)
    {
        // La columna `rango` guarda hora local sin zona (ARCHITECTURE §10.6):
        // el inicio del turno es `lower(rango)`. Los extremos viajan como
        // `timestamp` sin zona, igual que en DisponibilidadConsulta, para que
        // Npgsql no mezcle Kinds sobre una columna tsrange.
        var desdeLocal = new NpgsqlParameter("desdeLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(desdeUtc),
        };
        var hastaLocal = new NpgsqlParameter("hastaLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(hastaUtc),
        };

        return await _contexto.Turnos
            .FromSqlRaw(SqlTurnosDelDia, lavaderoId, desdeLocal, hastaLocal)
            .AsNoTracking()
            .ToArrayAsync(ct);
    }

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
            // conflicto se eleva al dominio para que la reserva lo maneje.
            throw new TurnoConflictoException(
                $"El puesto tiene otro turno en el mismo rango. (detalle: {postgres.MessageText})");
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException postgres &&
                  postgres.SqlState == SqlStateUniqueViolation &&
                  postgres.ConstraintName == IndiceIdempotencia)
        {
            // Reintento idéntico con la fila original fuera del predicado:
            // la unicidad de la clave dispara antes que la exclusión.
            throw new ReservaDuplicadaException(
                "Ya existe una reserva con la misma clave de idempotencia.",
                ex);
        }
    }
}
