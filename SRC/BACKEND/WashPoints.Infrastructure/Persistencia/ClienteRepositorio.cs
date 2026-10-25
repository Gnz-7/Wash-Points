using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Persistencia del cliente de negocio (slice-08, D-09.2).
///
/// <see cref="AgregarAsync"/> persiste en el acto: el alta corre dentro de la
/// transacción que abre <c>IUnidadDeTrabajo</c>, así que si la credencial no se
/// puede crear el rollback se lleva también a este cliente.
/// </summary>
public sealed class ClienteRepositorio : IClienteRepositorio
{
    private readonly WashPointsDbContext _contexto;

    public ClienteRepositorio(WashPointsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task AgregarAsync(Cliente cliente, CancellationToken ct)
    {
        await _contexto.Clientes.AddAsync(cliente, ct);
        await _contexto.SaveChangesAsync(ct);
    }
}
