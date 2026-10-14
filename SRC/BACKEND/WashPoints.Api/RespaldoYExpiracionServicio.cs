using Microsoft.EntityFrameworkCore;
using WashPoints.Application.Pagos;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;

namespace WashPoints.Api;

/// <summary>
/// Worker de conciliación de la seña (SPECS/slice-01 T-21). Corre cada
/// <see cref="OpcionesPago.IntervaloWorkerSegundos"/> y ejecuta dos reglas
/// independientes:
///
/// <list type="number">
/// <item>
/// <b>Expiración local</b> (CA-S1.10): los <c>pendiente_pago</c> con
/// <c>expira_en &lt;= now()</c> pasan a <c>expirado</c>, salen del predicado del
/// EXCLUDE y liberan el box (RB-2). No toca la pasarela.
/// </item>
/// <item>
/// <b>Respaldo</b> (CA-S1.7): los <c>pendiente_pago</c> que llevan más de
/// <see cref="OpcionesPago.VentanaRespaldoMinutos"/> sin resolverse se consultan
/// a la pasarela (<see cref="IPasarelaPagos.VerificarPagoAsync"/>); un
/// <c>approved</c>/<c>rejected</c> aplica la misma transición atómica que el
/// webhook.
/// </item>
/// </list>
///
/// <para>
/// <b>Aislamiento del ciclo local.</b> La expiración corre <b>primero</b> y no
/// depende de la red; cada llamada de la pasarela tiene su propio timeout y su
/// propio try/catch, de modo que un respaldo colgado o fallido no retrasa ni
/// impide la liberación de los boxes vencidos.
/// </para>
///
/// <para>
/// Vive en el Api (composition root) porque necesita tanto las opciones de la
/// capa Application (<see cref="OpcionesPago"/>) como la entidad de pago de
/// Infrastructure (<see cref="WebhookPagoServicio"/>). La alternativa —un puerto
/// de persistencia de pagos— rompería la atomicidad "pago + transición" en un
/// solo <c>SaveChangesAsync</c> al separar el staging del guardado.
/// </para>
/// </summary>
public sealed class RespaldoYExpiracionServicio
{
    private readonly WashPointsDbContext _contexto;
    private readonly IPasarelaPagos _pasarela;
    private readonly WebhookPagoServicio _aplicador;
    private readonly OpcionesPago _opciones;
    private readonly TimeProvider _reloj;
    private readonly IOutbox _outbox;

    public RespaldoYExpiracionServicio(
        WashPointsDbContext contexto,
        IPasarelaPagos pasarela,
        WebhookPagoServicio aplicador,
        OpcionesPago opciones,
        TimeProvider reloj,
        IOutbox outbox)
    {
        _contexto = contexto;
        _pasarela = pasarela;
        _aplicador = aplicador;
        _opciones = opciones;
        _reloj = reloj;
        _outbox = outbox;
    }

    /// <summary>Ejecuta un tick completo: expiración local y luego respaldo.</summary>
    public async Task<TickResultado> EjecutarTickAsync(CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow().UtcDateTime;

        var expirados = await ExpirarVencidosAsync(ahora, ct);
        var (respaldados, fallidos) = await RespaldarAsync(ahora, ct);

        return new TickResultado(expirados, respaldados, fallidos);
    }

    /// <summary>Regla 2: pendientes vencidos → expirado. Sin dependencia de red.</summary>
    public async Task<int> ExpirarVencidosAsync(DateTime ahoraUtc, CancellationToken ct)
    {
        var vencidos = await _contexto.Turnos
            .Where(t => t.Estado == EstadoTurno.PendientePago
                && t.ExpiraEn != null
                && t.ExpiraEn <= ahoraUtc)
            .ToListAsync(ct);

        foreach (var turno in vencidos)
        {
            turno.TransicionarA(EstadoTurno.Expirado);

            // RB-2 / CA-S3.8: liberar el box cambia la disponibilidad, así que
            // el evento de liberación se encola antes del SaveChangesAsync del
            // lote (D3/D12): turno y evento se persisten juntos. `Encolar` no
            // commitea por su cuenta.
            _outbox.Encolar(new EventoOcupacion(
                turno.Id,
                turno.LavaderoId,
                turno.PuestoId,
                turno.Origen,
                turno.Estado,
                turno.InicioUtc,
                turno.FinUtc));

            // SPECS/slice-04 §5.3: la expiración también cambia la agenda.
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
            await _contexto.SaveChangesAsync(ct);
        }

        return vencidos.Count;
    }

    /// <summary>
    /// Regla 1: consulta la pasarela para los pendientes que superaron la
    /// ventana de respaldo y aún están vigentes. Devuelve cuántos se resolvieron
    /// y cuántas consultas fallaron (que se reintentan en el próximo tick).
    /// </summary>
    public async Task<(int Respaldados, int Fallidos)> RespaldarAsync(
        DateTime ahoraUtc,
        CancellationToken ct)
    {
        var limite = ahoraUtc.AddMinutes(-_opciones.VentanaRespaldoMinutos);

        var candidatos = await _contexto.Turnos
            .Where(t => t.Estado == EstadoTurno.PendientePago
                && t.CreadoEn <= limite
                && t.ExpiraEn != null
                && t.ExpiraEn > ahoraUtc
                && t.MpPreferenceId != null)
            .ToListAsync(ct);

        if (candidatos.Count == 0)
        {
            return (0, 0);
        }

        var conPago = await _contexto.Pagos
            .Select(p => p.TurnoId)
            .ToListAsync(ct);
        var idsConPago = conPago.ToHashSet();

        var respaldados = 0;
        var fallidos = 0;

        foreach (var turno in candidatos)
        {
            if (idsConPago.Contains(turno.Id))
            {
                // El webhook ya procesó este pago: no se toca (idempotencia).
                continue;
            }

            // Timeout propio por llamada: un proveedor lento no retrasa el tick
            // ni el ciclo de expiración del resto (T-21).
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_opciones.TimeoutRespaldoSegundos));

            try
            {
                var verificacion = await _pasarela.VerificarPagoAsync(
                    turno.MpPreferenceId!, timeout.Token);

                var debeAplicar =
                    (verificacion.Estado == EstadoVerificacionPago.Aprobado ||
                     verificacion.Estado == EstadoVerificacionPago.Rechazado) &&
                    !string.IsNullOrWhiteSpace(verificacion.MpPaymentId);

                if (debeAplicar)
                {
                    await _aplicador.AplicarVerificacionAsync(
                        turno, verificacion.MpPaymentId!, verificacion.Estado, ct);
                    respaldados++;
                }
                // `pending`/`in_process`: no se hace nada; el TTL decide.
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Falla o timeout del respaldo: se registra y se reintenta en el
                // próximo tick, sin romper el tick ni la expiración local.
                fallidos++;
            }
        }

        return (respaldados, fallidos);
    }
}

/// <summary>Resumen de un tick del worker, útil para logs y tests.</summary>
public readonly record struct TickResultado(int Expirados, int Respaldados, int Fallidos);
