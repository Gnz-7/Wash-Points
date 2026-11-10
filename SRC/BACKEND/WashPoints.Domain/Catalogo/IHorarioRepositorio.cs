namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Persistencia de las franjas de atención de un lavadero (R-1,
/// SPECS/slice-17 D-17.1 y D-17.11).
///
/// El ABM reemplaza el conjunto completo de la semana en una sola operación:
/// el panel envía lo que el admin ve y no hay estados intermedios.
/// </summary>
public interface IHorarioRepositorio
{
    /// <summary>Franjas activas del lavadero, ordenadas por día y apertura.</summary>
    Task<IReadOnlyList<FranjaAtencion>> ListarAsync(Guid lavaderoId, CancellationToken ct);

    /// <summary>
    /// Agrega franjas sin guardar; el llamador decide cuándo persistir. Lo usa
    /// el alta de lavadero para sembrar los horarios por defecto dentro de la
    /// misma transacción que el resto del onboarding (D-17.2).
    /// </summary>
    Task AgregarRangoAsync(IEnumerable<FranjaAtencion> franjas, CancellationToken ct);

    /// <summary>
    /// Reemplaza el conjunto completo: desactiva las franjas vigentes del
    /// lavadero y escribe las recibidas, en un único guardado (D-17.11).
    /// Una lista vacía deja el lavadero cerrado.
    /// </summary>
    Task ReemplazarAsync(
        Guid lavaderoId,
        IReadOnlyList<FranjaAtencion> franjas,
        CancellationToken ct);

    /// <summary>Persiste los cambios pendientes de la unidad de trabajo.</summary>
    Task GuardarAsync(CancellationToken ct);
}
