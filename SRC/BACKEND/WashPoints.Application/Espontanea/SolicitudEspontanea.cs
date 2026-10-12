namespace WashPoints.Application.Espontanea;

/// <summary>
/// Solicitud de registro de demanda espontánea (CU-07, SPECS/slice-03 T-36).
/// No lleva cliente ni seña: el local atiende a quien llega sin turno previo.
/// </summary>
public sealed record SolicitudEspontanea(
    Guid LavaderoId,
    Guid PuestoId,
    Guid TipoLavadoId,
    DateTime InicioUtc);
