using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WashPoints.Application.Pagos;
using WashPoints.Domain.MercadoPago;
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
    private readonly IObtenedorTokensMp _tokensMp;
    private readonly WebhookPagoServicio _aplicador;
    private readonly OpcionesPago _opciones;
    private readonly TimeProvider _reloj;
    private readonly IOutbox _outbox;
    private readonly ILogger<RespaldoYExpiracionServicio> _logger;

    public RespaldoYExpiracionServicio(
        WashPointsDbContext contexto,
        IPasarelaPagos pasarela,
        IObtenedorTokensMp tokensMp,
        WebhookPagoServicio aplicador,
        OpcionesPago opciones,
        TimeProvider reloj,
        IOutbox outbox,
        ILogger<RespaldoYExpiracionServicio> logger)
    {
        _contexto = contexto;
        _pasarela = pasarela;
        _tokensMp = tokensMp;
        _aplicador = aplicador;
        _opciones = opciones;
        _reloj = reloj;
        _outbox = outbox;
        _logger = logger;
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
    /// ventana de respaldo y para los expirados recientes que todavía podrían
    /// confirmarse con un pago tardío. Devuelve cuántos se resolvieron y
    /// cuántas consultas fallaron (que se reintentan en el próximo tick).
    /// </summary>
    public async Task<(int Respaldados, int Fallidos)> RespaldarAsync(
        DateTime ahoraUtc,
        CancellationToken ct)
    {
        var limite = ahoraUtc.AddMinutes(-_opciones.VentanaRespaldoMinutos);
        // Cota de la conciliación tardía: no se consultan eternamente los
        // turnos expirados. 24 h cubre el día operativo y el reintento de MP.
        var limiteExpirados = ahoraUtc.AddHours(-24);

        var candidatos = await _contexto.Turnos
            .Where(t => t.MpPreferenceId != null
                && t.CreadoEn <= limite
                && t.ExpiraEn != null
                && ((t.Estado == EstadoTurno.PendientePago
                        && t.ExpiraEn > ahoraUtc)
                    || (t.Estado == EstadoTurno.Expirado
                        && t.ExpiraEn >= limiteExpirados)))
            .ToListAsync(ct);

        if (candidatos.Count == 0)
        {
            return (0, 0);
        }

        var respaldados = 0;
        var fallidos = 0;

        foreach (var turno in candidatos)
        {
            try
            {
                var resultado = await ConciliarTurnoAsync(turno, ct);
                if (resultado == ResultadoConciliacion.Aplicado)
                {
                    respaldados++;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Falla o timeout del respaldo: se registra y se reintenta en el
                // próximo tick, sin romper el tick ni la expiración local.
                fallidos++;
                _logger.LogWarning(
                    ex,
                    "No se pudo reconciliar el turno {TurnoId} con Mercado Pago.",
                    turno.Id);
            }
        }

        return (respaldados, fallidos);
    }

    /// <summary>
    /// Consulta la pasarela para un turno concreto y aplica el pago si ya está
    /// resuelto. Es la ruta única del worker y del endpoint de verificación en
    /// caliente (<c>POST /api/turnos/{id}/verificar-pago</c>): un solo lugar
    /// decide con qué token se consulta y cuándo se registra el pago (RB-1).
    ///
    /// Las excepciones de pasarela se propagan: el worker las cuenta como
    /// fallidas y el endpoint las mapea a 503/422.
    /// </summary>
    public async Task<ResultadoConciliacion> ConciliarTurnoAsync(
        Turno turno,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(turno.MpPreferenceId))
        {
            return ResultadoConciliacion.SinPreferencia;
        }

        // Un aprobado ya registrado es la fuente de verdad: si no pudo
        // confirmar (slot ocupado), queda para conciliación manual y no se
        // vuelve a insertar (el único parcial de approved lo impediría).
        var yaAprobado = await _contexto.Pagos
            .AnyAsync(p => p.TurnoId == turno.Id && p.Resultado == ResultadoPago.Approved, ct);
        if (yaAprobado)
        {
            return ResultadoConciliacion.YaRegistrado;
        }

        // Timeout propio por llamada: un proveedor lento no retrasa el tick
        // ni el ciclo de expiración del resto (T-21).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_opciones.TimeoutRespaldoSegundos));

        // Slice-20 (D-20.5): se consulta con el token del lavadero del turno.
        // Un token rechazado (401) refresca y reintenta una vez; un lavadero
        // desvinculado lanza y el llamador decide (tick: se reintenta; API: 422).
        var accessToken = await _tokensMp.ObtenerTokenVigenteAsync(
            turno.LavaderoId, timeout.Token);

        // La preferencia no es un payment id: se busca el pago por la
        // referencia externa del turno (slice-15 D-15.2/T-153).
        ResultadoVerificacionPago verificacion;

        try
        {
            verificacion = await _pasarela.BuscarPagoPorTurnoAsync(
                turno.Id, accessToken, timeout.Token);
        }
        catch (PasarelaTokenInvalidoException)
        {
            var tokenFresco = await _tokensMp.RefrescarTokenAsync(
                turno.LavaderoId, timeout.Token);
            verificacion = await _pasarela.BuscarPagoPorTurnoAsync(
                turno.Id, tokenFresco, timeout.Token);
        }

        var debeAplicar =
            (verificacion.Estado == EstadoVerificacionPago.Aprobado ||
             verificacion.Estado == EstadoVerificacionPago.Rechazado) &&
            !string.IsNullOrWhiteSpace(verificacion.MpPaymentId);

        if (!debeAplicar)
        {
            // `pending`/`in_process`: no hay nada que aplicar todavía.
            return ResultadoConciliacion.SinPago;
        }

        // Si ese pago ya está registrado (webhook o tick anterior), no se
        // vuelve a insertar: el UNIQUE de mp_payment_id lo rechazaría.
        var yaRegistrado = await _contexto.Pagos
            .AnyAsync(p => p.MpPaymentId == verificacion.MpPaymentId, ct);
        if (yaRegistrado)
        {
            return ResultadoConciliacion.YaRegistrado;
        }

        await _aplicador.AplicarVerificacionAsync(
            turno, verificacion.MpPaymentId!, verificacion.Estado, ct);

        return ResultadoConciliacion.Aplicado;
    }
}

/// <summary>Desenlace de una conciliación puntual contra la pasarela.</summary>
public enum ResultadoConciliacion
{
    /// <summary>La pasarela todavía no informa un pago resoluble.</summary>
    SinPago,

    /// <summary>Se registró el pago y, si correspondía, se transicionó el turno.</summary>
    Aplicado,

    /// <summary>El pago ya estaba registrado; no se duplicó.</summary>
    YaRegistrado,

    /// <summary>El turno no tiene preferencia de pago que consultar.</summary>
    SinPreferencia,
}

/// <summary>Resumen de un tick del worker, útil para logs y tests.</summary>
public readonly record struct TickResultado(int Expirados, int Respaldados, int Fallidos);
