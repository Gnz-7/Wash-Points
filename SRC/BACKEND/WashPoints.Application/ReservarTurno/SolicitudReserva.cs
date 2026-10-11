namespace WashPoints.Application.ReservarTurno;

/// <summary>
/// Datos de una solicitud de reserva, ya validados y con el inicio en UTC
/// (la conversión desde la hora local del cliente la hace la capa HTTP).
/// </summary>
public sealed record SolicitudReserva(
    Guid LavaderoId,
    Guid PuestoId,
    Guid ClienteId,
    Guid TipoLavadoId,
    DateTime InicioUtc);
