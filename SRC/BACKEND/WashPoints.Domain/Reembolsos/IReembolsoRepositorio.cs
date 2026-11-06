namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Puerto de persistencia de los reembolsos (SPECS/slice-14 D-14.7).
///
/// <para>
/// Un turno tiene a lo sumo un reembolso: la cancelación del cliente es una
/// sola por turno y la administrativa no genera fila (D-14.9). El repositorio
/// expone la lectura por turno para que la consulta «Mis turnos» pueda mostrar
/// el estado del reintegro sin un join manual.
/// </para>
/// </summary>
public interface IReembolsoRepositorio
{
    Task AgregarAsync(Reembolso reembolso, CancellationToken ct);

    Task<Reembolso?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct);
}
