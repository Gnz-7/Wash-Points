using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Reembolsos;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Persistencia de los reembolsos (SPECS/slice-14 T-143).
///
/// <para>
/// La escritura se suma al Change Tracker del <see cref="WashPointsDbContext"/>
/// compartido y la confirma la misma unidad de trabajo que mueve el turno, de
/// modo que la cancelación y su reembolso se guardan juntos (D-14.7).
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
}
