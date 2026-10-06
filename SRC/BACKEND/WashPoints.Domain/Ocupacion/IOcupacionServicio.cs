using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Única puerta de entrada para reservar o liberar un intervalo sobre un
/// puesto (ARCHITECTURE §2.2: módulo Ocupacion).
///
/// La transición de estado nunca la decide este servicio: delega en
/// <see cref="Turno.TransicionarA"/>. Lo que le corresponde es la operación de
/// persistencia atomizada —que el INSERT del turno y su confirmación ocurran
/// bajo la restricción EXCLUDE del motor.
/// </summary>
public interface IOcupacionServicio
{
    /// <summary>
    /// Crea un turno en <see cref="EstadoTurno.PendientePago"/>. No choca con
    /// nada: ocupa el puesto recién cuando se confirma (RB-1).
    /// </summary>
    Task<Guid> ReservarAsync(Turno turno, CancellationToken ct);

    /// <summary>
    /// Confirma la ocupación: transiciona a <see cref="EstadoTurno.Confirmado"/>
    /// y deja que la EXCLUDE de la base decida si el puesto estaba tomado.
    /// Lanza <see cref="TurnoConflictoException"/> si otro turno ocupaba el rango.
    /// </summary>
    Task ConfirmarOcupacionAsync(Guid turnoId, CancellationToken ct);
}