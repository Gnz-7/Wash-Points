namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puerto de escritura de clientes (SPECS/slice-03 T-35/D8).
///
/// La demanda espontánea necesita persistir un <see cref="Cliente"/> para
/// satisfacer `turnos.cliente_id NOT NULL` sin cliente real. Es un mecanismo
/// <b>provisorio</b> hasta que se formalice el esquema de `clientes`
/// (DOCS/ARCHITECTURE.md §10.7).
///
/// Al igual que <see cref="Ocupacion.IOutbox"/>, no dispara su propio
/// `SaveChangesAsync`: escribe por el Change Tracker de la unidad de trabajo.
/// </summary>
public interface IRegistroClientes
{
    Task AgregarAsync(Cliente cliente, CancellationToken ct);
}
