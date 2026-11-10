using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <inheritdoc />
public sealed class ConsultaCatalogo : IConsultaCatalogo
{
    private readonly WashPointsDbContext _contexto;

    public ConsultaCatalogo(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<TipoLavado?> ObtenerTipoLavadoAsync(
        Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct) =>
        await _contexto.TiposLavado
            .AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.Id == tipoLavadoId && t.LavaderoId == lavaderoId && t.Activo,
                ct);

    public async Task<Puesto?> ObtenerPuestoAsync(
        Guid lavaderoId, Guid puestoId, CancellationToken ct) =>
        await _contexto.Puestos
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == puestoId && p.LavaderoId == lavaderoId && p.Activo,
                ct);

    public async Task<bool> PuestoAtiendeTipoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, CancellationToken ct)
    {
        var tipos = await (
                from puente in _contexto.PuestosTiposLavado.AsNoTracking()
                join puesto in _contexto.Puestos.AsNoTracking()
                    on puente.PuestoId equals puesto.Id
                where puesto.Id == puestoId && puesto.LavaderoId == lavaderoId
                select puente.TipoLavadoId)
            .ToArrayAsync(ct);

        // Sin filas de compatibilidad, el puesto atiende todos los servicios
        // (slice-17, D-17.7).
        return tipos.Length == 0 || tipos.Contains(tipoLavadoId);
    }

    public async Task<CatalogoLavadero?> ObtenerCatalogoAsync(Guid lavaderoId, CancellationToken ct)
    {
        var existe = await _contexto.Lavaderos
            .AsNoTracking()
            .AnyAsync(l => l.Id == lavaderoId, ct);

        if (!existe)
        {
            return null;
        }

        var puestos = await _contexto.Puestos
            .AsNoTracking()
            .Where(p => p.LavaderoId == lavaderoId && p.Activo)
            .OrderBy(p => p.Nombre)
            .ToListAsync(ct);

        var tipos = await _contexto.TiposLavado
            .AsNoTracking()
            .Where(t => t.LavaderoId == lavaderoId && t.Activo)
            .OrderBy(t => t.Nombre)
            .ToListAsync(ct);

        // Compatibilidad puesto ↔ tipo (slice-17, D-17.10): los puestos sin
        // filas no aparecen en el mapa y el catálogo los interpreta como
        // «atiende todos los servicios» (D-17.7).
        var filas = await (
                from puente in _contexto.PuestosTiposLavado.AsNoTracking()
                join puesto in _contexto.Puestos.AsNoTracking()
                    on puente.PuestoId equals puesto.Id
                where puesto.LavaderoId == lavaderoId && puesto.Activo
                select new { puente.PuestoId, puente.TipoLavadoId })
            .ToArrayAsync(ct);

        var tiposPorPuesto = filas
            .GroupBy(f => f.PuestoId)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => (IReadOnlyList<Guid>)grupo
                    .Select(f => f.TipoLavadoId)
                    .ToArray());

        return new CatalogoLavadero(puestos, tipos, tiposPorPuesto);
    }
}
