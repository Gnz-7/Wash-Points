using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <inheritdoc />
public sealed class ConsultaCatalogo : IConsultaCatalogo
{
    private readonly WashPointsDbContext _contexto;

    public ConsultaCatalogo(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<TipoLavado?> ObtenerTipoLavadoAsync(Guid tipoLavadoId, CancellationToken ct) =>
        await _contexto.TiposLavado
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == tipoLavadoId, ct);

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

        return new CatalogoLavadero(puestos, tipos);
    }
}
