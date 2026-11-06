using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.MisTurnos;

/// <summary>
/// Fila del historial de turnos de un cliente (SPECS/slice-14 D-14.1).
///
/// Es un modelo de lectura: no hay lógica de negocio acá. El cálculo de
/// <c>cancelable</c>, del reintegro estimado y de la disponibilidad del
/// comprobante vive en <c>MisTurnosServicio</c>, que es el único que conoce la
/// política de reembolso y los estados terminales (D-14.6, D-14.12).
/// </summary>
/// <param name="TurnoId">Identificador del turno.</param>
/// <param name="LavaderoId">Lavadero donde se atiende.</param>
/// <param name="LavaderoNombre">Nombre comercial del lavadero.</param>
/// <param name="Direccion">Dirección declarada, si la hay.</param>
/// <param name="TipoLavado">Nombre del tipo de lavado contratado.</param>
/// <param name="DuracionMinutos">Duración del servicio, en minutos.</param>
/// <param name="InicioUtc">Inicio del turno, en UTC.</param>
/// <param name="FinUtc">Fin del turno, en UTC.</param>
/// <param name="Estado">Estado actual del turno.</param>
/// <param name="PrecioTotal">Precio total congelado al reservar.</param>
/// <param name="PrecioSena">Seña cobrada al reservar.</param>
/// <param name="TieneComprobante">Existe un comprobante emitido para el turno.</param>
/// <param name="ReembolsoEstado">Estado del reembolso, si el turno tiene uno.</param>
/// <param name="ReembolsoMotivo">Motivo del reembolso, si el turno tiene uno.</param>
/// <param name="ReembolsoMonto">Monto del reembolso, si el turno tiene uno.</param>
public sealed record TurnoCliente(
    Guid TurnoId,
    Guid LavaderoId,
    string LavaderoNombre,
    string? Direccion,
    string TipoLavado,
    int DuracionMinutos,
    DateTime InicioUtc,
    DateTime FinUtc,
    EstadoTurno Estado,
    decimal PrecioTotal,
    decimal PrecioSena,
    bool TieneComprobante,
    EstadoReembolso? ReembolsoEstado,
    MotivoReembolso? ReembolsoMotivo,
    decimal? ReembolsoMonto);
