using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Comprobantes;
using WashPoints.Infrastructure.Persistencia;

namespace WashPoints.Infrastructure.Comprobantes;

/// <summary>
/// Implementación EF Core de <see cref="IComprobanteRepositorio"/> (SPECS/slice-02 T-27).
/// </summary>
public sealed class ComprobanteRepositorio : IComprobanteRepositorio
{
    private readonly WashPointsDbContext _contexto;

    public ComprobanteRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<Comprobante?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
        await _contexto.Comprobantes
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.TurnoId == turnoId, ct);

    public async Task<Comprobante?> ObtenerPorHashAsync(string hash, CancellationToken ct) =>
        await _contexto.Comprobantes
            .SingleOrDefaultAsync(c => c.Hash == hash, ct);

    public Task<int> MarcarCanjeadoSiDisponibleAsync(
        Guid comprobanteId,
        DateTime usadoEnUtc,
        bool offline,
        CancellationToken ct) =>
        _contexto.Comprobantes
            .Where(c => c.Id == comprobanteId && c.UsadoAt == null)
            .ExecuteUpdateAsync(
                establecimiento => establecimiento
                    .SetProperty(c => c.UsadoAt, usadoEnUtc)
                    .SetProperty(c => c.ValidadoOffline, offline),
                ct);

    public async Task<IReadOnlyList<string>> ListarHashesVigentesAsync(
        IReadOnlyCollection<Guid> turnoIds,
        DateTime ahoraUtc,
        CancellationToken ct)
    {
        if (turnoIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        return await _contexto.Comprobantes
            .AsNoTracking()
            .Where(c => turnoIds.Contains(c.TurnoId) && c.UsadoAt == null && c.ExpiraEn > ahoraUtc)
            .Select(c => c.Hash)
            .ToListAsync(ct);
    }
}
