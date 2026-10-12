using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de escritura de clientes (SPECS/slice-03 T-36/D8).
///
/// Respeta el contrato D12: solo agrega el cliente al Change Tracker, sin
/// <c>SaveChangesAsync</c> propio. El commit lo hace la unidad de trabajo que
/// envuelve la operación.
/// </summary>
public sealed class RegistroClientes : IRegistroClientes
{
    private readonly WashPointsDbContext _contexto;

    public RegistroClientes(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task AgregarAsync(Cliente cliente, CancellationToken ct) =>
        await _contexto.Clientes.AddAsync(cliente, ct);
}
