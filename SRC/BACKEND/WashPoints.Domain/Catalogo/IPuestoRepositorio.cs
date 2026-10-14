namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puerto de persistencia de puestos de lavado (SPECS/slice-04 T-42, CU-05).
///
/// Todo lo que escribe en la tabla `puestos` pasa por acá. Igual que
/// <see cref="Ocupacion.IOutbox"/> y <see cref="IRegistroClientes"/>, no dispara
/// su propio `SaveChangesAsync`: escribe por el Change Tracker de la unidad de
/// trabajo (<see cref="GuardarAsync"/>).
/// </summary>
public interface IPuestoRepositorio
{
    Task AgregarAsync(Puesto puesto, CancellationToken ct);

    Task<Puesto?> ObtenerAsync(Guid lavaderoId, Guid puestoId, CancellationToken ct);

    Task<IReadOnlyList<Puesto>> ListarAsync(Guid lavaderoId, CancellationToken ct);

    Task GuardarAsync(CancellationToken ct);
}
