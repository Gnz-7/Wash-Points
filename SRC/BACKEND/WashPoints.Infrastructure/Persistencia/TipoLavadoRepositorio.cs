using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de tipos de lavado (SPECS/slice-13 T-131).
///
/// El índice único <c>(lavadero_id, nombre)</c> rechaza nombres duplicados con
/// SQLSTATE 23505; <see cref="GuardarAsync"/> lo traduce a
/// <see cref="TipoLavadoDuplicadoException"/> para que la capa HTTP responda
/// 409 <c>tipo_ya_existe</c> (D-13.10).
/// </summary>
public sealed class TipoLavadoRepositorio : ITipoLavadoRepositorio
{
    private const string SqlStateUniqueViolation = "23505";
    private const string TablaTiposLavado = "tipos_lavado";

    private readonly WashPointsDbContext _contexto;

    public TipoLavadoRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task AgregarAsync(TipoLavado tipo, CancellationToken ct) =>
        await _contexto.TiposLavado.AddAsync(tipo, ct);

    public async Task<TipoLavado?> ObtenerAsync(
        Guid lavaderoId,
        Guid tipoId,
        CancellationToken ct) =>
        await _contexto.TiposLavado.SingleOrDefaultAsync(
            t => t.LavaderoId == lavaderoId && t.Id == tipoId, ct);

    public async Task<IReadOnlyList<TipoLavado>> ListarAsync(
        Guid lavaderoId,
        CancellationToken ct) =>
        await _contexto.TiposLavado
            .AsNoTracking()
            .Where(t => t.LavaderoId == lavaderoId)
            .OrderBy(t => t.Nombre)
            .ToArrayAsync(ct);

    public async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await _contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException postgres &&
                  postgres.SqlState == SqlStateUniqueViolation &&
                  postgres.TableName == TablaTiposLavado)
        {
            throw new TipoLavadoDuplicadoException(
                "Ya existe un tipo de lavado con ese nombre en el lavadero.", ex);
        }
    }
}
