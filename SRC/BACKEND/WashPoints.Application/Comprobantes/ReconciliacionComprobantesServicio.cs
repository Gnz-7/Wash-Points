using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Comprobantes;

/// <summary>Canje registrado sin red por el dispositivo del administrador.</summary>
public sealed record CanjeOffline(string Hash, DateTime ValidadoEn);

/// <summary>Canje offline aplicado sobre el turno correspondiente.</summary>
public sealed record CanjeAplicado(Guid TurnoId, string Hash);

/// <summary>Canje offline que el servidor no reconoció como válido.</summary>
public sealed record CanjeRechazado(string Hash, string Motivo);

/// <summary>Resultado de un lote de reconciliación.</summary>
public sealed record ResultadoReconciliacion(
    IReadOnlyList<CanjeAplicado> Aplicados,
    IReadOnlyList<CanjeRechazado> Rechazados);

/// <summary>
/// Reconciliación de canjes registrados offline (SPECS/slice-02 T-31; RB-3,
/// US-A03 CA-3.4/CA-3.5).
///
/// El servidor es la autoridad: cada entrada del lote se valida de forma
/// independiente contra el comprobante y el turno reales. Un canje que el
/// servidor no reconoce válido <b>no</b> altera el estado del turno y se informa
/// como rechazado. Los canjes válidos se aplican con <c>validado_offline = true</c>.
/// </summary>
public sealed class ReconciliacionComprobantesServicio
{
    private readonly IOcupacionRepositorio _turnos;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly TimeProvider _reloj;

    public ReconciliacionComprobantesServicio(
        IOcupacionRepositorio turnos,
        IComprobanteRepositorio comprobantes,
        IUnidadDeTrabajo unidadDeTrabajo,
        TimeProvider reloj)
    {
        _turnos = turnos;
        _comprobantes = comprobantes;
        _unidadDeTrabajo = unidadDeTrabajo;
        _reloj = reloj;
    }

    public async Task<ResultadoReconciliacion> ReconciliarAsync(
        Guid lavaderoId,
        IReadOnlyList<CanjeOffline> canjes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(canjes);

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var aplicados = new List<CanjeAplicado>();
        var rechazados = new List<CanjeRechazado>();

        foreach (var canje in canjes)
        {
            var motivo = await IntentarAplicarAsync(lavaderoId, canje, ahora, aplicados, ct);
            if (motivo is not null)
            {
                rechazados.Add(new CanjeRechazado(canje.Hash, motivo));
            }
        }

        return new ResultadoReconciliacion(aplicados, rechazados);
    }

    private async Task<string?> IntentarAplicarAsync(
        Guid lavaderoId,
        CanjeOffline canje,
        DateTime ahora,
        List<CanjeAplicado> aplicados,
        CancellationToken ct)
    {
        // El timestamp del dispositivo puede adelantarse: nunca se registra en el
        // futuro respecto del servidor (autoridad).
        var cuando = canje.ValidadoEn > ahora ? ahora : canje.ValidadoEn;

        try
        {
            var aplicado = await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async token =>
            {
                var registro = await _comprobantes.ObtenerPorHashAsync(canje.Hash, token)
                    ?? throw new ComprobanteInvalidoException("Desconocido.");

                var turno = await _turnos.ObtenerAsync(registro.TurnoId, token)
                    ?? throw new ComprobanteInvalidoException("Turno inexistente.");

                // CA-S2.10: otro lavadero ⇒ rechazo sin filtrar datos.
                if (turno.LavaderoId != lavaderoId)
                {
                    throw new ComprobanteInvalidoException("Otro lavadero.");
                }

                registro.AsegurarCanjeable(ahora);

                if (turno.Estado == EstadoTurno.PendientePago)
                {
                    throw new ComprobantePagoPendienteException(turno.Id);
                }

                // Un turno ya en curso implica que el comprobante fue canjeado: se
                // informa "ya utilizado" y no se altera el turno.
                if (turno.Estado == EstadoTurno.EnCurso)
                {
                    throw new ComprobanteYaUtilizadoException(turno.Id);
                }

                if (turno.Estado != EstadoTurno.Confirmado)
                {
                    throw new ComprobanteInvalidoException("Estado no canjeable.");
                }

                var filas = await _comprobantes.MarcarCanjeadoSiDisponibleAsync(
                    registro.Id, cuando, offline: true, token);
                if (filas == 0)
                {
                    throw new ComprobanteYaUtilizadoException(registro.TurnoId);
                }

                turno.TransicionarA(EstadoTurno.EnCurso);
                await _turnos.GuardarAsync(token);

                return new CanjeAplicado(turno.Id, canje.Hash);
            }, ct);

            aplicados.Add(aplicado);
            return null;
        }
        catch (ComprobanteYaUtilizadoException)
        {
            return "comprobante_ya_utilizado";
        }
        catch (ComprobanteVencidoException)
        {
            return "comprobante_vencido";
        }
        catch (ComprobantePagoPendienteException)
        {
            return "pago_pendiente";
        }
        catch (ComprobanteInvalidoException)
        {
            return "comprobante_no_encontrado";
        }
    }
}
