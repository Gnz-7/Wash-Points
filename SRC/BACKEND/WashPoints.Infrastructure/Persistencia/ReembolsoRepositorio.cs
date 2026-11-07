using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Reembolsos;
using WashPoints.Infrastructure.Pagos;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Persistencia de los reembolsos (SPECS/slice-14 T-143, slice-15 T-155).
///
/// <para>
/// La escritura se suma al Change Tracker del <see cref="WashPointsDbContext"/>
/// compartido y la confirma la misma unidad de trabajo que mueve el turno, de
/// modo que la cancelación y su reembolso se guardan juntos (D-14.7).
/// </para>
///
/// <para>
/// El worker de ejecución usa <see cref="ReclamarPendientesAsync"/>: un
/// <c>UPDATE ... RETURNING</c> con <c>FOR UPDATE SKIP LOCKED</c> que suma el
/// intento y fija el lease en una sola sentencia. Es lo que impide que dos
/// ticks (o dos instancias) reembolsen dos veces el mismo turno (D-15.4).
/// </para>
/// </summary>
public sealed class ReembolsoRepositorio : IReembolsoRepositorio
{
    private readonly WashPointsDbContext _contexto;

    public ReembolsoRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task AgregarAsync(Reembolso reembolso, CancellationToken ct) =>
        await _contexto.Reembolsos.AddAsync(reembolso, ct);

    public Task<Reembolso?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
        _contexto.Reembolsos
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.TurnoId == turnoId, ct);

    public async Task<IReadOnlyList<Reembolso>> ReclamarPendientesAsync(
        int lote,
        DateTime ahoraUtc,
        TimeSpan lease,
        CancellationToken ct)
    {
        var limiteLease = ahoraUtc - lease;

        // PostgreSQL evalúa el UPDATE y el SELECT bajo el mismo snapshot; el
        // SKIP LOCKED hace que un worker concurrente salte las filas tomadas en
        // vez de esperarlas. RETURNING * trae la fila ya reclamada para que EF
        // la materialice con el intento incrementado.
        return await _contexto.Reembolsos
            .FromSqlInterpolated($"""
                UPDATE reembolsos
                SET reclamado_en = {ahoraUtc},
                    intentos = intentos + 1
                WHERE id IN (
                    SELECT id
                    FROM reembolsos
                    WHERE estado = 'pendiente'
                      AND (proximo_intento_en IS NULL OR proximo_intento_en <= {ahoraUtc})
                      AND (reclamado_en IS NULL OR reclamado_en < {limiteLease})
                    ORDER BY creado_en
                    LIMIT {lote}
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING *;
                """)
            .AsTracking()
            .ToListAsync(ct);
    }

    public Task<string?> ObtenerPagoAprobadoAsync(Guid turnoId, CancellationToken ct) =>
        _contexto.Pagos
            .Where(p => p.TurnoId == turnoId && p.Resultado == ResultadoPago.Approved)
            .OrderByDescending(p => p.ProcesadoEn)
            .Select(p => p.MpPaymentId)
            .FirstOrDefaultAsync(ct);

    public Task GuardarCambiosAsync(CancellationToken ct) =>
        _contexto.SaveChangesAsync(ct);
}
