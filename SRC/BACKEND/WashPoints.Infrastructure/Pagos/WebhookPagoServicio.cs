using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.MercadoPago;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Reembolsos;
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
    private readonly IObtenedorTokensMp _tokensMp;
    private readonly IOcupacionRepositorio _turnos;
    private readonly WashPointsDbContext _contexto;
    private readonly IEmisorComprobante _emisorComprobante;
    private readonly IOutbox _outbox;
    private readonly TimeProvider _reloj;

    public WebhookPagoServicio(
        IPasarelaPagos pasarela,
        IObtenedorTokensMp tokensMp,
        IOcupacionRepositorio turnos,
        WashPointsDbContext contexto,
        IEmisorComprobante emisorComprobante,
        IOutbox outbox,
        TimeProvider? reloj = null)
    {
        _pasarela = pasarela;
        _tokensMp = tokensMp;
        _turnos = turnos;
        _contexto = contexto;
        _emisorComprobante = emisorComprobante;
        _outbox = outbox;
        _reloj = reloj ?? TimeProvider.System;
    }

    /// <summary>
    /// Procesa una notificación ya autenticada.
    /// </summary>
    /// <param name="tipo">Campo <c>type</c> del IPN (<c>payment</c>, <c>refund</c>…).</param>
    /// <param name="accion">Campo <c>action</c> del IPN (<c>payment.refunded</c>…), si vino.</param>
    /// <param name="dataId">Identificador del recurso notificado.</param>
    /// <param name="userId">Campo <c>user_id</c> del payload (la cuenta de MP
    /// que cobró: el lavadero). Puede venir como número o string.</param>
    /// <param name="ct">Cancelación.</param>
    public async Task<ResultadoWebhook> ProcesarAsync(
        string? tipo,
        string? accion,
        string dataId,
        string? userId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataId);

        // Un evento de reintegro es una señal, no una fuente de verdad: sólo
        // adelanta el trabajo del worker, que consulta el estado real a la
        // pasarela (D-15.6). Nunca se toma el monto del payload.
        if (EsEventoDeReembolso(tipo, accion))
        {
            return await SenalarReembolsoAsync(dataId, ct);
        }

        if (!string.Equals(tipo, "payment", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoWebhook.Ignorado;
        }

        // slice-20 (D-20.5): el pago vive en la cuenta de Mercado Pago del
        // lavadero, así que la verificación se hace con el token de ESA
        // cuenta. El lavadero se resuelve antes de verificar: primero por el
        // pago ya registrado (duplicados y fuera de orden) y, si no hay fila,
        // por el user_id del payload.
        var lavaderoId = await ResolverLavaderoDelPagoAsync(dataId, userId, ct);
        if (lavaderoId is null)
        {
            return ResultadoWebhook.TurnoNoEncontrado;
        }

        var accessToken = await _tokensMp.ObtenerTokenVigenteAsync(lavaderoId.Value, ct);

        ResultadoVerificacionPago verificacion;
        try
        {
            verificacion = await _pasarela.VerificarPagoAsync(dataId, accessToken, ct);
        }
        catch (PasarelaTokenInvalidoException)
        {
            // Token expirado o revocado: un refresco y un solo reintento
            // (CA-S20.15). Un refresh fallido desvincula y lanza
            // MercadoPagoNoVinculadoException, que el endpoint mapea.
            var fresco = await _tokensMp.RefrescarTokenAsync(lavaderoId.Value, ct);
            verificacion = await _pasarela.VerificarPagoAsync(dataId, fresco, ct);
        }
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

        // Un pendiente_pago se resuelve con el pago. Un turno expirado sólo
        // revive si el pago aprobado llegó tarde pero el turno todavía no
        // terminó; si el puesto ya lo tomó otro, el EXCLUDE lo rechaza y el
        // pago queda para conciliación manual. Un rechazo tardío no cancela un
        // turno ya expirado.
        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var esPendiente = turno.Estado == EstadoTurno.PendientePago;
        var esExpirado = turno.Estado == EstadoTurno.Expirado;
        var confirmaTardio = esExpirado && turno.FinUtc > ahora;

        if (estado == EstadoVerificacionPago.Aprobado && (esPendiente || confirmaTardio))
        {
            turno.TransicionarA(EstadoTurno.Confirmado);

            // RB-3: la confirmación emite el comprobante en la MISMA unidad
            // de trabajo. Si la emisión falla (p. ej. falta la clave de
            // cifrado), no se persiste ni el pago ni la confirmación.
            _emisorComprobante.Emitir(turno.Id, turno.ConfirmadoEn!.Value);

            // RB-2: el cambio de disponibilidad y su evento viajan en el
            // mismo SaveChangesAsync (D3/D12). `Encolar` no commitea.
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
        else if (estado == EstadoVerificacionPago.Rechazado && esPendiente)
        {
            turno.TransicionarA(EstadoTurno.Cancelado);

            // RB-2 (SPECS/slice-04 §5.3): el rechazo libera el pendiente que
            // bloqueaba el puesto, así que emite ocupación; y todo cambio de
            // estado emite agenda. Mismo SaveChangesAsync que la transición.
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

        try
        {
            // Un solo SaveChangesAsync: pago + transición atómicos (T-17).
            await _turnos.GuardarAsync(ct);
            return esExpirado && estado == EstadoVerificacionPago.Aprobado && !confirmaTardio
                ? ResultadoWebhook.PagoSinConfirmar
                : ResultadoWebhook.Procesado;
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
            // UNIQUE parcial (turno_id) WHERE resultado = 'approved': el turno ya
            // tenía un pago aprobado (notificación fuera de orden con otro
            // payment_id). Idempotente: 200 sin efecto (T-18).
            return ResultadoWebhook.Duplicado;
        }
    }

    /// <summary>
    /// Resuelve el lavadero dueño del pago <b>antes</b> de verificarlo
    /// (D-20.5): primero por un pago ya registrado con ese payment id (cubre
    /// duplicados y notificaciones fuera de orden) y, si no hay fila, por el
    /// <c>user_id</c> del payload (el <c>mp_user_id</c> en claro del lavadero,
    /// que puede venir como número o como string). Sin lavadero, la
    /// notificación no puede verificarse con la cuenta correcta: se responde
    /// <c>TurnoNoEncontrado</c> para que Mercado Pago reintente.
    /// </summary>
    private async Task<Guid?> ResolverLavaderoDelPagoAsync(
        string dataId,
        string? userId,
        CancellationToken ct)
    {
        var turnoId = await _contexto.Pagos
            .Where(p => p.MpPaymentId == dataId)
            .Select(p => (Guid?)p.TurnoId)
            .FirstOrDefaultAsync(ct);

        if (turnoId is not null)
        {
            var turno = await _turnos.ObtenerAsync(turnoId.Value, ct);
            if (turno is not null)
            {
                return turno.LavaderoId;
            }
        }

        var normalizado = userId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizado))
        {
            return null;
        }

        var porUsuario = await _contexto.Lavaderos
            .Where(l => l.MpUserId == normalizado)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(ct);
        if (porUsuario is not null)
        {
            return porUsuario;
        }

        // El payload de Mercado Pago trae el user_id numérico sin comillas;
        // en la base se guardó como string. Se compara la forma numérica.
        if (long.TryParse(normalizado, out var numero))
        {
            return await _contexto.Lavaderos
                .Where(l => l.MpUserId == numero.ToString())
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private static ResultadoPago Mapear(EstadoVerificacionPago estado) => estado switch
    {
        EstadoVerificacionPago.Aprobado => ResultadoPago.Approved,
        EstadoVerificacionPago.Rechazado => ResultadoPago.Rejected,
        _ => ResultadoPago.Pending,
    };

    private static bool EsEventoDeReembolso(string? tipo, string? accion) =>
        (tipo?.Contains("refund", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (accion?.Contains("refund", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Marca como vencido el reembolso pendiente que corresponde al evento.
    /// Se busca por <c>mp_refund_id</c> y, si el evento trae un payment id, por
    /// el pago aprobado del turno. Si no hay reembolso pendiente no hay nada
    /// que hacer: el evento es idempotente y responde 200.
    /// </summary>
    private async Task<ResultadoWebhook> SenalarReembolsoAsync(string dataId, CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        var pendientes = await _contexto.Reembolsos
            .Where(r => r.Estado == EstadoReembolso.Pendiente && r.MpRefundId == dataId)
            .ToListAsync(ct);

        if (pendientes.Count == 0)
        {
            var turnoId = await _contexto.Pagos
                .Where(p => p.MpPaymentId == dataId)
                .Select(p => (Guid?)p.TurnoId)
                .FirstOrDefaultAsync(ct);

            if (turnoId is not null)
            {
                pendientes = await _contexto.Reembolsos
                    .Where(r => r.Estado == EstadoReembolso.Pendiente && r.TurnoId == turnoId.Value)
                    .ToListAsync(ct);
            }
        }

        if (pendientes.Count == 0)
        {
            return ResultadoWebhook.Ignorado;
        }

        foreach (var reembolso in pendientes)
        {
            reembolso.SenalarVencimiento(ahora);
        }

        await _contexto.SaveChangesAsync(ct);
        return ResultadoWebhook.Procesado;
    }
}
