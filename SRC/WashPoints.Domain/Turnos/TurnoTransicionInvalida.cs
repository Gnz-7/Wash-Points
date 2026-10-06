namespace WashPoints.Domain.Turnos;

/// <summary>
/// Se lanza cuando se intenta una transición de estado que la tabla de
/// DOCS/ARCHITECTURE.md §5 no autoriza.
///
/// Es el mecanismo que protege RB-1: cualquier atajo que llevara un turno sin
/// seña a ocupar un puesto termina aquí.
/// </summary>
public sealed class TurnoTransicionInvalida : Exception
{
    public EstadoTurno Desde { get; }
    public EstadoTurno Hacia { get; }

    public TurnoTransicionInvalida(EstadoTurno desde, EstadoTurno hacia)
        : base($"Transición inválida de turno: {desde} → {hacia}.")
    {
        Desde = desde;
        Hacia = hacia;
    }
}
