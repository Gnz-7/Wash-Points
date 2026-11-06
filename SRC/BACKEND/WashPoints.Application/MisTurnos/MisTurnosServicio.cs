using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Turnos;

using WashPoints.Domain.MisTurnos;

namespace WashPoints.Application.MisTurnos;

/// <summary>
/// Arma el listado «Mis turnos» aplicando la política de reembolso
/// (SPECS/slice-14 §6, D-14.1 y D-14.6).
///
/// <para>
/// La consulta sólo trae datos; las decisiones de negocio se toman acá: si el
/// turno es cancelable, cuánto se reintegraría si el cliente cancelara ahora y
/// si el comprobante sigue siendo utilizable. Así el cliente ve el mismo
/// número que obtendría al cancelar, y no una estimación distinta.
/// </para>
/// </summary>
public sealed class MisTurnosServicio
{
    private readonly IConsultaMisTurnos _consulta;
    private readonly PoliticaReembolso _politica;

    public MisTurnosServicio(IConsultaMisTurnos consulta, PoliticaReembolso politica)
    {
        _consulta = consulta;
        _politica = politica;
    }

    /// <summary>
    /// Lista los turnos del cliente evaluados contra el momento recibido.
    /// </summary>
    /// <param name="clienteId">Cliente tomado del token, nunca de la ruta.</param>
    /// <param name="ahoraUtc">Instante de la consulta, en UTC.</param>
    public async Task<IReadOnlyList<TurnoClienteVm>> ListarAsync(
        Guid clienteId,
        DateTime ahoraUtc,
        CancellationToken ct)
    {
        var turnos = await _consulta.ListarAsync(clienteId, ct);

        return turnos.Select(turno => Evaluar(turno, ahoraUtc)).ToArray();
    }

    private TurnoClienteVm Evaluar(TurnoCliente turno, DateTime ahoraUtc)
    {
        var cancelable = turno.Estado is EstadoTurno.PendientePago or EstadoTurno.Confirmado;

        // El comprobante sólo sirve para validar el arribo de un turno
        // confirmado; en un turno cerrado o en curso ya no hay nada que
        // presentar en el mostrador (D-14.12).
        var comprobanteDisponible =
            turno.TieneComprobante && turno.Estado == EstadoTurno.Confirmado;

        if (!cancelable)
        {
            return new TurnoClienteVm(
                turno,
                comprobanteDisponible,
                Cancelable: false,
                ReembolsoEstimado: 0m,
                ReembolsoMotivo: null);
        }

        // Un turno que todavía no pagó la seña no devuelve dinero, igual que en
        // la cancelación real (motivo sin_pago).
        var evaluacion = turno.Estado == EstadoTurno.PendientePago
            ? new EvaluacionReembolso(EstadoReembolso.NoAplica, MotivoReembolso.SinPago, 0m)
            : _politica.Evaluar(turno.PrecioSena, turno.InicioUtc, ahoraUtc);

        return new TurnoClienteVm(
            turno,
            comprobanteDisponible,
            Cancelable: true,
            evaluacion.Monto,
            evaluacion.Motivo);
    }
}
