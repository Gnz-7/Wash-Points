using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Intervalo ocupado de un puesto, tal como lo ve un cliente consultando
/// disponibilidad (US-C05 CA-5.1).
///
/// Los extremos se expresan en UTC (ARCHITECTURE §10.6): la capa que expone el
/// endpoint los traduce a la hora local del lavadero. El <see cref="Origen"/>
/// distingue reservas de app de la demanda espontánea del admin: ambas
/// escribieron la misma tabla, la fuente no se pierde (RB-2).
/// </summary>
public sealed record IntervaloOcupado
{
    public IntervaloOcupado(
        Guid turnoId,
        Guid puestoId,
        OrigenTurno origen,
        DateTime inicioUtc,
        DateTime finUtc)
    {
        TurnoId = turnoId;
        PuestoId = puestoId;
        Origen = origen;
        InicioUtc = inicioUtc;
        FinUtc = finUtc;
    }

    public Guid TurnoId { get; }
    public Guid PuestoId { get; }
    public OrigenTurno Origen { get; }
    public DateTime InicioUtc { get; }
    public DateTime FinUtc { get; }
}