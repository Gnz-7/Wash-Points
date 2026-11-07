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

    /// <summary>
    /// Reclama hasta <paramref name="lote"/> reembolsos pendientes listos para
    /// procesar, de forma atómica y sin bloquear a otro worker
    /// (<c>FOR UPDATE SKIP LOCKED</c>). Suma un intento y fija el lease en la
    /// misma sentencia (D-15.4).
    /// </summary>
    Task<IReadOnlyList<Reembolso>> ReclamarPendientesAsync(
        int lote,
        DateTime ahoraUtc,
        TimeSpan lease,
        CancellationToken ct);

    /// <summary>
    /// Identificador del pago aprobado del turno, que es el que hay que
    /// reembolsar. <c>null</c> si el turno nunca se cobró.
    /// </summary>
    Task<string?> ObtenerPagoAprobadoAsync(Guid turnoId, CancellationToken ct);

    /// <summary>Confirma los cambios del tick del worker.</summary>
    Task GuardarCambiosAsync(CancellationToken ct);
}
