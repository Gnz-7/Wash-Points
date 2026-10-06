namespace WashPoints.Domain.Turnos;

/// <summary>
/// Origen de la ocupación. Ambos valores escriben sobre la misma tabla de
/// `turnos`: esa es la garantía de RB-2 (ARCHITECTURE §6).
/// </summary>
public enum OrigenTurno
{
    /// <summary>Reserva hecha desde la app por el cliente.</summary>
    App,

    /// <summary>Carga manual del admin para un cliente sin turno previo.</summary>
    Espontanea,
}
