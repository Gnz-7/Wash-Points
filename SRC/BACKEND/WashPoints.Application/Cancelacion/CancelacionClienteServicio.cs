using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Seguridad;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Cancelacion;

/// <summary>
/// Cancelación de un turno pedida por el cliente (SPECS/slice-14, CU-02).
///
/// <para>
/// <b>Pertenencia.</b> La identidad sale siempre del token, nunca de la ruta:
/// un turno de otro cliente se trata como inexistente para no filtrar la
/// existencia de reservas ajenas (D-06.3, D-14.3).
/// </para>
/// <para>
/// <b>Política.</b> El monto del reintegro lo decide <see cref="PoliticaReembolso"/>
/// con el inicio del turno como referencia (D-14.6). Un turno que todavía no
/// pagó la seña no genera reintegro: se registra <c>no_aplica / sin_pago</c>.
/// </para>
/// <para>
/// <b>Atomicidad.</b> El turno, el reembolso y los dos eventos de la outbox se
/// persisten con un único <c>GuardarAsync</c>, igual que
/// <c>AgendaServicio</c> (RB-2, D-04.10). La cancelación no llama a la
/// pasarela: el reembolso queda <c>pendiente</c> para el slice de pagos
/// (D-14.8).
/// </para>
/// </summary>
public sealed class CancelacionClienteServicio
{
    private readonly IOcupacionRepositorio _turnos;
    private readonly IReembolsoRepositorio _reembolsos;
    private readonly PoliticaReembolso _politica;
    private readonly IOutbox _outbox;

    public CancelacionClienteServicio(
        IOcupacionRepositorio turnos,
        IReembolsoRepositorio reembolsos,
        PoliticaReembolso politica,
        IOutbox outbox)
    {
        _turnos = turnos;
        _reembolsos = reembolsos;
        _politica = politica;
        _outbox = outbox;
    }

    /// <summary>
    /// Cancela el turno si es del cliente autenticado.
    /// </summary>
    /// <returns>
    /// El turno cancelado y su reembolso, o <c>null</c> cuando el turno no
    /// existe, es de otro cliente o la identidad no es de un cliente.
    /// </returns>
    /// <exception cref="TurnoTransicionInvalida">
    /// El turno está <c>en_curso</c> o en un estado terminal (D-14.4).
    /// </exception>
    public async Task<ResultadoCancelacion?> CancelarAsync(
        Guid turnoId,
        IdentidadUsuario? identidad,
        DateTime ahoraUtc,
        CancellationToken ct)
    {
        if (identidad?.ClienteId is not { } clienteId)
        {
            return null;
        }

        var turno = await _turnos.ObtenerAsync(turnoId, ct);
        if (turno is null || turno.ClienteId != clienteId)
        {
            return null;
        }

        // Un pendiente_pago no pagó la seña: no hay nada que reintegrar aunque
        // esté dentro del plazo (D-14.6, motivo sin_pago).
        var evaluacion = turno.Estado == EstadoTurno.PendientePago
            ? new EvaluacionReembolso(EstadoReembolso.NoAplica, MotivoReembolso.SinPago, 0m)
            : _politica.Evaluar(turno.PrecioSena, turno.InicioUtc, ahoraUtc);

        turno.CancelarPorCliente();

        var reembolso = Reembolso.Registrar(
            turno.Id,
            evaluacion.Estado,
            evaluacion.Motivo,
            evaluacion.Monto,
            ahoraUtc);

        await _reembolsos.AgregarAsync(reembolso, ct);

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

        return new ResultadoCancelacion(turno, reembolso);
    }
}
