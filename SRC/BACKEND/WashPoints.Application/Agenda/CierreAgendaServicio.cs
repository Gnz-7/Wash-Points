using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Agenda;

/// <summary>
/// Cierre de agenda por ventana de llegada (CU-06, US-A06 CA-6.4; D2/D-04.2).
///
/// Barre los turnos <c>confirmado</c> cuyo inicio ya cayó más allá de
/// <see cref="OpcionesAgenda.VentanaLlegadaMinutos"/> y los lleva a
/// <c>no_presentado</c>, encolando los eventos de §5.3 en el mismo commit
/// (RB-2). Es un turno terminal, así que un canje posterior no lo revive.
///
/// La regla vive acá —y no en el worker— para poder testearla sin reloj real.
/// El worker de tiempo real solo aporta el <c>ahora</c> y el intervalo.
/// </summary>
public sealed class CierreAgendaServicio
{
    private readonly IOcupacionRepositorio _turnos;
    private readonly IOutbox _outbox;
    private readonly OpcionesAgenda _opciones;

    public CierreAgendaServicio(
        IOcupacionRepositorio turnos,
        IOutbox outbox,
        OpcionesAgenda opciones)
    {
        _turnos = turnos;
        _outbox = outbox;
        _opciones = opciones;
    }

    /// <summary>
    /// Marca como <c>no_presentado</c> los confirmados vencidos y devuelve
    /// cuántos cerró. Un solo <c>SaveChangesAsync</c> para todo el lote.
    /// </summary>
    public async Task<int> EjecutarTickAsync(DateTime ahoraUtc, CancellationToken ct)
    {
        var limiteUtc = ahoraUtc.AddMinutes(-_opciones.VentanaLlegadaMinutos);
        var vencidos = await _turnos.ListarConfirmadosVencidosAsync(limiteUtc, ct);

        foreach (var turno in vencidos)
        {
            // Terminal: en_curso (canjeado antes del tick) no está en esta
            // consulta, así que la carrera la gana el canje (D-04.2).
            turno.TransicionarA(EstadoTurno.NoPresentado);

            // RB-2 (§5.3): no_presentado libera el puesto (ocupación) y cambia
            // la agenda. Ambos viajan antes del flush del lote.
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
        }

        if (vencidos.Count > 0)
        {
            await _turnos.GuardarAsync(ct);
        }

        return vencidos.Count;
    }
}
