namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Resultado de aplicar la política de reembolso a un turno concreto
/// (SPECS/slice-14 D-14.6).
///
/// Es un valor inmutable: la política decide, el servicio de aplicación
/// persiste. No conoce la pasarela de pagos (D-14.8).
/// </summary>
/// <param name="Estado">Estado con el que nace la fila de <c>reembolsos</c>.</param>
/// <param name="Motivo">Causa del reintegro, para auditoría.</param>
/// <param name="Monto">Importe a reintegrar, ya redondeado a dos decimales.</param>
public sealed record EvaluacionReembolso(
    EstadoReembolso Estado,
    MotivoReembolso Motivo,
    decimal Monto);
