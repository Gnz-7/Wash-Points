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
}
