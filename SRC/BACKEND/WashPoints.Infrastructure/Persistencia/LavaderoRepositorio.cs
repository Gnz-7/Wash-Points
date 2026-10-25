using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Persistencia del lavadero (slice-08, D-09.3).
///
/// <see cref="AgregarAsync"/> persiste en el acto: el alta del administrador
/// corre dentro de la transacción que abre <c>IUnidadDeTrabajo</c>, así que un
/// nombre de usuario tomado revierte también el lavadero recién creado. Nunca
/// queda un lavadero huérfano.
/// </summary>
public sealed class LavaderoRepositorio : ILavaderoRepositorio
{
    private readonly WashPointsDbContext _contexto;

    public LavaderoRepositorio(WashPointsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task AgregarAsync(Lavadero lavadero, CancellationToken ct)
    {
        await _contexto.Lavaderos.AddAsync(lavadero, ct);
        await _contexto.SaveChangesAsync(ct);
    }
}
