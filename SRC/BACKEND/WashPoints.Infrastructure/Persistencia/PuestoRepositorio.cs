using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de puestos de lavado (SPECS/slice-04 T-42/T-43).
///
/// El índice único <c>(lavadero_id, nombre)</c> rechaza nombres duplicados con
/// SQLSTATE 23505; <see cref="GuardarAsync"/> lo traduce a
/// <see cref="PuestoDuplicadoException"/> para que la capa HTTP responda 409
/// <c>puesto_ya_existe</c> (CA-S4.5).
/// </summary>
public sealed class PuestoRepositorio : IPuestoRepositorio
{
    private const string SqlStateUniqueViolation = "23505";

    private readonly WashPointsDbContext _contexto;

    public PuestoRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task AgregarAsync(Puesto puesto, CancellationToken ct) =>
        await _contexto.Puestos.AddAsync(puesto, ct);

    public async Task<Puesto?> ObtenerAsync(Guid lavaderoId, Guid puestoId, CancellationToken ct) =>
        await _contexto.Puestos.SingleOrDefaultAsync(
            p => p.LavaderoId == lavaderoId && p.Id == puestoId, ct);

    public async Task<IReadOnlyList<Puesto>> ListarAsync(Guid lavaderoId, CancellationToken ct) =>
        await _contexto.Puestos
            .AsNoTracking()
            .Where(p => p.LavaderoId == lavaderoId)
            .OrderBy(p => p.Nombre)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyList<Guid>> ObtenerTiposAsync(
        Guid puestoId, CancellationToken ct) =>
        await _contexto.PuestosTiposLavado
            .AsNoTracking()
            .Where(x => x.PuestoId == puestoId)
            .Select(x => x.TipoLavadoId)
            .ToArrayAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ObtenerTiposPorPuestoAsync(
        Guid lavaderoId, CancellationToken ct)
    {
        var filas = await (
                from puente in _contexto.PuestosTiposLavado.AsNoTracking()
                join puesto in _contexto.Puestos.AsNoTracking()
                    on puente.PuestoId equals puesto.Id
                where puesto.LavaderoId == lavaderoId
                select new { puente.PuestoId, puente.TipoLavadoId })
            .ToArrayAsync(ct);

        return filas
            .GroupBy(f => f.PuestoId)
            .ToDictionary(
                grupo => grupo.Key,
                grupo => (IReadOnlyList<Guid>)grupo
                    .Select(f => f.TipoLavadoId)
                    .ToArray());
    }

    public async Task ReemplazarTiposAsync(
        Guid puestoId, IReadOnlyList<Guid> tiposLavadoIds, CancellationToken ct)
    {
        var nuevas = tiposLavadoIds.Distinct().ToHashSet();

        var vigentes = await _contexto.PuestosTiposLavado
            .Where(x => x.PuestoId == puestoId)
            .ToListAsync(ct);

        // Se borra sólo lo que sobra y se agrega sólo lo que falta: así el
        // guardado no depende del orden en que EF ejecute DELETE e INSERT
        // sobre la misma clave compuesta.
        _contexto.PuestosTiposLavado.RemoveRange(
            vigentes.Where(x => !nuevas.Contains(x.TipoLavadoId)));

        var existentes = vigentes.Select(x => x.TipoLavadoId).ToHashSet();

        foreach (var tipoLavadoId in nuevas.Where(id => !existentes.Contains(id)))
        {
            await _contexto.PuestosTiposLavado.AddAsync(
                PuestoTipoLavado.Crear(puestoId, tipoLavadoId), ct);
        }
    }

    public async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await _contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException postgres &&
                  postgres.SqlState == SqlStateUniqueViolation &&
                  postgres.TableName == "puestos")
        {
            throw new PuestoDuplicadoException(
                "Ya existe un puesto con ese nombre en el lavadero.", ex);
        }
    }
}
