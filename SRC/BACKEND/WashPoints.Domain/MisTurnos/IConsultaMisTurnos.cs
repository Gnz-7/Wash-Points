namespace WashPoints.Domain.MisTurnos;

/// <summary>
/// Lectura del historial de turnos de un cliente (SPECS/slice-14 D-14.1).
///
/// Es un puerto de sólo lectura: no arbitra ocupación ni escribe en
/// <c>turnos</c>. Vive en el dominio, igual que el resto de las consultas
/// (<c>IConsultaMetricas</c>, <c>IConsultaAgenda</c>), para que la
/// infraestructura pueda implementarlo sin depender de la capa de aplicación.
/// </summary>
public interface IConsultaMisTurnos
{
    /// <summary>
    /// Turnos del cliente, del más reciente al más viejo.
    /// </summary>
    Task<IReadOnlyList<TurnoCliente>> ListarAsync(Guid clienteId, CancellationToken ct);
}
