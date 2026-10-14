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
