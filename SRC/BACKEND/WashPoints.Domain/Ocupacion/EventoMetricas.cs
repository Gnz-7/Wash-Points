namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Señal de invalidación del panel de métricas (slice-18, D-18.2).
///
/// Viaja por la outbox con el tipo <c>metricas_cambiada</c> y se publica por
/// SignalR al grupo <c>metricas-{id}</c> del lavadero. <b>No lleva importes,
/// conteos ni PII</b>: el cliente la trata como aviso de que su foto quedó
/// vieja y vuelve a pedir el resumen y la ocupación por REST (una sola fuente
/// de verdad).
///
/// <para>
/// <see cref="Fecha"/> es el día <b>local</b> del turno afectado y es
/// <c>null</c> cuando el cambio no proviene de un turno (alta, activación o
/// desactivación de un puesto). <see cref="GeneradoEn"/> es el instante UTC de
/// emisión.
/// </para>
/// </summary>
public sealed record EventoMetricas(
    Guid LavaderoId,
    DateOnly? Fecha,
    DateTime GeneradoEn);
