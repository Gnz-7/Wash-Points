using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Cancelacion;

/// <summary>
/// Resultado de una cancelación pedida por el cliente (SPECS/slice-14 D-14.3).
///
/// <para>
/// Devuelve el turno ya cancelado y el reembolso registrado en la misma
/// operación. El reembolso nunca es <c>null</c>: incluso cuando no corresponde
/// reintegro se deja la fila en <see cref="EstadoReembolso.NoAplica"/> para
/// explicar el histórico (D-14.7).
/// </para>
/// </summary>
public sealed record ResultadoCancelacion(Turno Turno, Reembolso Reembolso);
