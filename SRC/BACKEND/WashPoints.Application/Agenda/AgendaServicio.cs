using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Agenda;

/// <summary>
/// Agenda del lavadero y ciclo de vida del turno (CU-06, SPECS/slice-04 T-46).
///
/// La lectura delega en <see cref="IConsultaAgenda"/> (solo join, sin
/// arbitraje). Las transiciones pasan siempre por <see cref="Turno.TransicionarA"/>
/// —nunca por un setter de estado— y encolan los eventos de la matriz §5.3 antes
/// del único <c>SaveChangesAsync</c>, de modo que turno y outbox se persisten
/// juntos (RB-2, D-04.10). Un turno de otro lavadero se trata como inexistente
/// (aislamiento, D-04.7).
/// </summary>
public sealed class AgendaServicio
{
    private readonly IConsultaAgenda _agenda;
    private readonly IOcupacionRepositorio _turnos;
    private readonly IOutbox _outbox;

    public AgendaServicio(
        IConsultaAgenda agenda,
        IOcupacionRepositorio turnos,
        IOutbox outbox)
    {
        _agenda = agenda;
        _turnos = turnos;
        _outbox = outbox;
    }

    /// <summary>Turnos del lavadero en <c>[desdeUtc, hastaUtc)</c>, ordenados por hora.</summary>
    public Task<IReadOnlyList<TurnoAgenda>> ObtenerAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        Guid? puestoId,
        EstadoTurno? estado,
        CancellationToken ct) =>
        _agenda.ListarAsync(lavaderoId, desdeUtc, hastaUtc, puestoId, estado, ct);

    /// <summary><c>en_curso → finalizado</c>. <c>null</c> si el turno no existe en el lavadero.</summary>
    public Task<Turno?> FinalizarAsync(Guid lavaderoId, Guid turnoId, CancellationToken ct) =>
        TransicionarAsync(lavaderoId, turnoId, EstadoTurno.Finalizado, ct);

    /// <summary><c>confirmado → cancelado</c>. <c>null</c> si el turno no existe en el lavadero.</summary>
    public Task<Turno?> CancelarAsync(Guid lavaderoId, Guid turnoId, CancellationToken ct) =>
        TransicionarAsync(lavaderoId, turnoId, EstadoTurno.Cancelado, ct);

    /// <summary><c>confirmado → no_presentado</c>. <c>null</c> si el turno no existe en el lavadero.</summary>
    public Task<Turno?> MarcarNoPresentadoAsync(Guid lavaderoId, Guid turnoId, CancellationToken ct) =>
        TransicionarAsync(lavaderoId, turnoId, EstadoTurno.NoPresentado, ct);

    private async Task<Turno?> TransicionarAsync(
        Guid lavaderoId,
        Guid turnoId,
        EstadoTurno destino,
        CancellationToken ct)
    {
        var turno = await _turnos.ObtenerAsync(turnoId, ct);

        // Aislamiento (D-04.7): un turno de otro lavadero es indistinguible de
        // inexistente; el endpoint lo mapea a 404 sin revelar su existencia.
        if (turno is null || turno.LavaderoId != lavaderoId)
        {
            return null;
        }

        // Única vía de cambio de estado; una transición no listada lanza
        // TurnoTransicionInvalida, que el endpoint mapea a 409 (RB-1).
        turno.TransicionarA(destino);

        // RB-2 (SPECS/slice-04 §5.3): finalizar, cancelar y no_presentado liberan
        // el puesto (ocupación) y siempre cambian la agenda. Un solo commit.
        _outbox.Encolar(new EventoOcupacion(
            turno.Id,
            turno.LavaderoId,
            turno.PuestoId,
            turno.Origen,
            turno.Estado,
            turno.InicioUtc,
            turno.FinUtc));
        _outbox.Encolar(new EventoAgenda(
            turno.Id,
            turno.LavaderoId,
            turno.PuestoId,
            turno.Origen,
            turno.Estado,
            turno.InicioUtc,
            turno.FinUtc));

        await _turnos.GuardarAsync(ct);

        return turno;
    }
}
