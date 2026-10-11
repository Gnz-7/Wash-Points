using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;

namespace WashPoints.Infrastructure.Pagos;

/// <summary>Desenlace del procesamiento de una notificación IPN.</summary>
public enum ResultadoWebhook
{
    /// <summary>Se registró el pago y (si correspondía) se transicionó el turno.</summary>
    Procesado,

    /// <summary>Notificación ya procesada (mismo <c>mp_payment_id</c>) o fuera de orden.</summary>
    Duplicado,

    /// <summary>Tipo de evento no soportado (p. ej. <c>plan</c>); no hay nada que hacer.</summary>
    Ignorado,

    /// <summary>El pago no pudo asociarse a un turno conocido.</summary>
    TurnoNoEncontrado,

    /// <summary>
    /// El pago aprobado no pudo confirmar: el slot lo tomó otra vía. El pago
    /// queda registrado para conciliación manual (T-17).
    /// </summary>
    PagoSinConfirmar,
}

/// <summary>
/// Orquesta el webhook IPN de Mercado Pago (SPECS/slice-01 T-17..T-19).
///
/// El módulo <c>Pagos</c> no decide la transición: consulta el estado a la
/// pasarela y <b>notifica</b> al turno (<c>Turno.TransicionarA</c>), que es
/// quien valida contra su máquina de estados (ARCHITECTURE §2.2). La
/// transición y el INSERT del <see cref="Pago"/> viajan en el mismo
/// <c>SaveChangesAsync</c>: o hay pago y turno consistente, o no hay nada.
///
/// La firma se valida <b>antes</b> de llegar acá (el endpoint devuelve 401);
/// este servicio asume una notificación ya autenticada.
/// </summary>
public sealed class WebhookPagoServicio
{
    private readonly IPasarelaPagos _pasarela;
    private readonly IOcupacionRepositorio _turnos;
    private readonly WashPointsDbContext _contexto;

    public WebhookPagoServicio(
        IPasarelaPagos pasarela,
        IOcupacionRepositorio turnos,
        WashPointsDbContext contexto)
    {
        _pasarela = pasarela;
        _turnos = turnos;
        _contexto = contexto;
    }

    public async Task<ResultadoWebhook> ProcesarAsync(string? tipo, string dataId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataId);

        if (!string.Equals(tipo, "payment", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoWebhook.Ignorado;
        }

        var verificacion = await _pasarela.VerificarPagoAsync(dataId, ct);
        var mpPaymentId = string.IsNullOrWhiteSpace(verificacion.MpPaymentId)
            ? dataId
            : verificacion.MpPaymentId;

        // Idempotencia a nivel de fila: el mismo pago ya procesado responde 200
        // sin re-transicionar (T-18).
        if (await _contexto.Pagos.AnyAsync(p => p.MpPaymentId == mpPaymentId, ct))
        {
            return ResultadoWebhook.Duplicado;
        }

        if (verificacion.TurnoId is not { } turnoId)
        {
            return ResultadoWebhook.TurnoNoEncontrado;
        }

        var turno = await _turnos.ObtenerAsync(turnoId, ct);
        if (turno is null)
        {
            return ResultadoWebhook.TurnoNoEncontrado;
        }

        return await AplicarVerificacionAsync(
            turno, mpPaymentId, verificacion.Estado, ct);
    }

    /// <summary>
    /// Aplica una verificación de pago ya resuelta sobre un turno: registra el
    /// <see cref="Pago"/> y, si corresponde, transiciona el turno, todo en un
    /// solo <c>SaveChangesAsync</c> (T-17). Es compartido por el webhook y el
    /// respaldo del worker (T-21) para que ambos caminos tengan idéntica
    /// atomicidad e idempotencia.
    /// </summary>
    public async Task<ResultadoWebhook> AplicarVerificacionAsync(
        Turno turno,
        string mpPaymentId,
        EstadoVerificacionPago estado,
        CancellationToken ct)
    {
        var resultado = Mapear(estado);
        _contexto.Pagos.Add(
            Pago.Registrar(turno.Id, mpPaymentId, TipoEventoPago.Payment, resultado));

        // Solo un pendiente_pago puede moverse. Un turno ya confirmado/cancelado
        // o vencido no revive por un pago tardío (RB-1).
        if (turno.Estado == EstadoTurno.PendientePago)
        {
            if (estado == EstadoVerificacionPago.Aprobado)
            {
                turno.TransicionarA(EstadoTurno.Confirmado);
            }
            else if (estado == EstadoVerificacionPago.Rechazado)
            {
                turno.TransicionarA(EstadoTurno.Cancelado);
            }
        }

        try
        {
            // Un solo SaveChangesAsync: pago + transición atómicos (T-17).
            await _turnos.GuardarAsync(ct);
            return ResultadoWebhook.Procesado;
        }
        catch (TurnoConflictoException)
        {
            // El slot lo ocupó otra vía: se descarta la transición y se guarda
            // solo el pago, para conciliación manual.
            _contexto.ChangeTracker.Clear();
            _contexto.Pagos.Add(
                Pago.Registrar(turno.Id, mpPaymentId, TipoEventoPago.Payment, resultado));
            await _contexto.SaveChangesAsync(ct);
            return ResultadoWebhook.PagoSinConfirmar;
        }
        catch (DbUpdateException)
        {
            // UNIQUE(turno_id): el turno ya tenía un pago (notificación fuera de
            // orden con otro payment_id). Idempotente: 200 sin efecto (T-18).
            return ResultadoWebhook.Duplicado;
        }
    }

    private static ResultadoPago Mapear(EstadoVerificacionPago estado) => estado switch
    {
        EstadoVerificacionPago.Aprobado => ResultadoPago.Approved,
        EstadoVerificacionPago.Rechazado => ResultadoPago.Rejected,
        _ => ResultadoPago.Pending,
    };
}
