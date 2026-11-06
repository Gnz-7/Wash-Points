using WashPoints.Domain.Reembolsos;

using WashPoints.Domain.MisTurnos;

namespace WashPoints.Application.MisTurnos;

/// <summary>
/// Turno del cliente ya evaluado para la pantalla «Mis turnos»
/// (SPECS/slice-14 §6, D-14.1).
///
/// Las reglas viven acá, no en el cliente: <see cref="Cancelable"/> y el
/// reintegro estimado los decide el servidor con la misma política que usa la
/// cancelación real (D-14.6), de modo que el número que ve el cliente sea
/// exactamente el que va a recibir.
/// </summary>
public sealed record TurnoClienteVm(
    TurnoCliente Turno,
    bool ComprobanteDisponible,
    bool Cancelable,
    decimal ReembolsoEstimado,
    MotivoReembolso? ReembolsoMotivo);
