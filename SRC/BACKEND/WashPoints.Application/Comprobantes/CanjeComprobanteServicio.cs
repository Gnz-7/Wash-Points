using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Comprobantes;

/// <summary>Resultado de un canje exitoso.</summary>
public sealed record ResultadoCanje(Guid TurnoId, EstadoTurno Estado, DateTime UsadoEn);

/// <summary>
/// Canje online del comprobante en destino (SPECS/slice-02 T-28; RB-3, US-A02/US-A04).
///
/// Recibe el payload QR o el código de respaldo, resuelve el token canónico,
/// localiza el comprobante por hash y, si el turno está confirmado y el
/// comprobante es del lavadero y no está usado ni vencido, marca <c>usado_at</c>
/// y aplica <c>confirmado → en_curso</c> en una sola transacción.
///
/// El gate de unicidad es el UPDATE condicional (<c>usado_at IS NULL</c>): bajo
/// concurrencia exactamente un canje gana y el resto recibe
/// <see cref="ComprobanteYaUtilizadoException"/> (CA-S2.6).
/// </summary>
public sealed class CanjeComprobanteServicio
{
    private readonly IOcupacionRepositorio _turnos;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly IGeneradorComprobante _generador;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly IOutbox _outbox;
    private readonly TimeProvider _reloj;

    public CanjeComprobanteServicio(
        IOcupacionRepositorio turnos,
        IComprobanteRepositorio comprobantes,
        IGeneradorComprobante generador,
        IUnidadDeTrabajo unidadDeTrabajo,
        IOutbox outbox,
        TimeProvider reloj)
    {
        _turnos = turnos;
        _comprobantes = comprobantes;
        _generador = generador;
        _unidadDeTrabajo = unidadDeTrabajo;
        _outbox = outbox;
        _reloj = reloj;
    }

    public async Task<ResultadoCanje> CanjearAsync(
        Guid lavaderoId,
        string comprobante,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(comprobante);

        var token = NormalizarToken(comprobante);
        var hash = _generador.CalcularHash(token);
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        return await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async token_cancelacion =>
        {
            var registro = await _comprobantes.ObtenerPorHashAsync(hash, token_cancelacion)
                ?? throw NoEncontrado();

            var turno = await _turnos.ObtenerAsync(registro.TurnoId, token_cancelacion)
                ?? throw NoEncontrado();

            // CA-S2.10: un comprobante de otro lavadero es indistinguible de uno
            // inexistente: no se revela si el token existe (CU-08).
            if (turno.LavaderoId != lavaderoId)
            {
                throw NoEncontrado();
            }

            // CA-S2.5 / CA-S2.7: "ya usado" tiene prioridad sobre "vencido".
            registro.AsegurarCanjeable(ahora);

            // CA-S2.4: sin seña confirmada no hay atención.
            if (turno.Estado == EstadoTurno.PendientePago)
            {
                throw new ComprobantePagoPendienteException(turno.Id);
            }

            // CA-S2.6: bajo concurrencia el perdedor puede leer el comprobante
            // todavía sin usar (snapshot previo al commit del ganador) y el turno
            // ya en curso. Un turno en curso implica que el comprobante fue
            // canjeado: se responde "ya utilizado", no "no encontrado".
            if (turno.Estado == EstadoTurno.EnCurso)
            {
                throw new ComprobanteYaUtilizadoException(turno.Id);
            }

            if (turno.Estado != EstadoTurno.Confirmado)
            {
                throw NoEncontrado();
            }

            // CA-S2.6: gate atómico. Exactamente un canje obtiene 1 fila.
            var filas = await _comprobantes.MarcarCanjeadoSiDisponibleAsync(
                registro.Id, ahora, offline: false, token_cancelacion);
            if (filas == 0)
            {
                throw new ComprobanteYaUtilizadoException(registro.TurnoId);
            }

            turno.TransicionarA(EstadoTurno.EnCurso);

            // RB-2 (SPECS/slice-04 §5.3): el canje no cambia la ocupación (el
            // turno ya ocupaba), pero sí la agenda. En el mismo commit que la
            // transición (D-04.10).
            _outbox.Encolar(new EventoAgenda(
                turno.Id,
                turno.LavaderoId,
                turno.PuestoId,
                turno.Origen,
                turno.Estado,
                turno.InicioUtc,
                turno.FinUtc));

            await _turnos.GuardarAsync(token_cancelacion);

            return new ResultadoCanje(turno.Id, turno.Estado, ahora);
        }, ct);
    }

    private string NormalizarToken(string identificador) =>
        identificador.StartsWith(IGeneradorComprobante.PrefijoPayloadQr, StringComparison.Ordinal)
            ? _generador.ExtraerTokenDePayloadQr(identificador)
            : _generador.DecodificarCodigoRespaldo(identificador);

    private static ComprobanteInvalidoException NoEncontrado() =>
        new("El comprobante no corresponde a ningún turno de este lavadero.");
}
