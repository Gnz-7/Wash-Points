using WashPoints.Domain.Puertos;
using WashPoints.Domain.Reembolsos;

namespace WashPoints.Application.Reembolsos;

/// <summary>
/// Ejecuta contra la pasarela los reembolsos que la cancelación dejó en
/// <see cref="EstadoReembolso.Pendiente"/> (SPECS/slice-15 D-15.4/T-156).
///
/// <para>
/// Un tick reclama un lote de forma atómica, resuelve el pago aprobado del
/// turno y pide el reintegro con una clave de idempotencia estable
/// (<c>reembolso-{id}</c>). La pasarela es la única fuente de verdad: sólo se
/// marca <see cref="EstadoReembolso.Procesado"/> cuando confirma. Un fallo
/// transitorio programa el reintento con backoff; un rechazo definitivo o el
/// agotamiento de intentos deja el reembolso en
/// <see cref="EstadoReembolso.Fallido"/>.
/// </para>
///
/// <para>
/// Un reembolso fallido no revierte el turno: la cancelación ya es un hecho y
/// el dinero pendiente es una tarea de conciliación, no una razón para
/// resucitar el turno (D-15.4).
/// </para>
/// </summary>
public sealed class ProcesarReembolsosServicio
{
    private const string SinPagoAprobado = "El turno no tiene un pago aprobado que reembolsar.";

    private readonly IReembolsoRepositorio _reembolsos;
    private readonly IPasarelaPagos _pasarela;
    private readonly OpcionesReembolso _opciones;
    private readonly Func<DateTime> _relojUtc;
    private readonly Func<Guid, int, int> _jitterSegundos;

    public ProcesarReembolsosServicio(
        IReembolsoRepositorio reembolsos,
        IPasarelaPagos pasarela,
        OpcionesReembolso opciones)
        : this(reembolsos, pasarela, opciones, () => DateTime.UtcNow, JitterDeterminista)
    {
    }

    /// <summary>
    /// Constructor con reloj y jitter inyectables: los tests fijan el instante y
    /// el desfase para que el backoff sea reproducible.
    /// </summary>
    public ProcesarReembolsosServicio(
        IReembolsoRepositorio reembolsos,
        IPasarelaPagos pasarela,
        OpcionesReembolso opciones,
        Func<DateTime> relojUtc,
        Func<Guid, int, int> jitterSegundos)
    {
        _reembolsos = reembolsos;
        _pasarela = pasarela;
        _opciones = opciones;
        _relojUtc = relojUtc;
        _jitterSegundos = jitterSegundos;
    }

    /// <summary>
    /// Procesa un lote. Nunca propaga un fallo de un reembolso individual: lo
    /// registra en la fila y sigue con el resto.
    /// </summary>
    public async Task<ResultadoProcesamientoReembolsos> EjecutarTickAsync(CancellationToken ct)
    {
        var ahora = _relojUtc();
        var lease = TimeSpan.FromMinutes(_opciones.LeaseMinutos);

        var lote = await _reembolsos.ReclamarPendientesAsync(
            _opciones.LoteMaximo, ahora, lease, ct);

        var procesados = 0;
        var reintentados = 0;
        var fallidos = 0;

        foreach (var reembolso in lote)
        {
            var resultado = await ProcesarAsync(reembolso, ahora, ct);

            switch (resultado)
            {
                case ResultadoItem.Procesado:
                    procesados++;
                    break;
                case ResultadoItem.Reintentado:
                    reintentados++;
                    break;
                default:
                    fallidos++;
                    break;
            }
        }

        await _reembolsos.GuardarCambiosAsync(ct);

        return new ResultadoProcesamientoReembolsos(procesados, reintentados, fallidos);
    }

    private async Task<ResultadoItem> ProcesarAsync(
        Reembolso reembolso,
        DateTime ahora,
        CancellationToken ct)
    {
        var pagoId = await _reembolsos.ObtenerPagoAprobadoAsync(reembolso.TurnoId, ct);

        if (string.IsNullOrWhiteSpace(pagoId))
        {
            reembolso.MarcarFallido(SinPagoAprobado);
            return ResultadoItem.Fallido;
        }

        ResultadoReembolso resultado;

        try
        {
            // Si el proceso cayó después de que la pasarela aceptara el
            // reintegro, se reconcilia por su id en vez de pedirlo otra vez.
            resultado = reembolso.MpRefundId is { Length: > 0 } reembolsoId
                ? await _pasarela.ConsultarReembolsoAsync(pagoId, reembolsoId, ct)
                : await _pasarela.ReembolsarAsync(
                    pagoId,
                    reembolso.Monto,
                    $"reembolso-{reembolso.Id}",
                    ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // La fila ya está reclamada: se libera programando el reintento.
            return Reintentar(reembolso, ahora, ex.Message);
        }

        return resultado.Estado switch
        {
            EstadoReembolsoPasarela.Aprobado when resultado.ReembolsoId is { Length: > 0 } id =>
                Confirmar(reembolso, id, ahora),

            // Sin id no hay confirmación posible: se trata como pendiente.
            EstadoReembolsoPasarela.Aprobado =>
                Reintentar(reembolso, ahora, "La pasarela aprobó sin devolver el id del reintegro."),

            EstadoReembolsoPasarela.Pendiente =>
                ReintentarPendiente(reembolso, ahora, resultado),

            EstadoReembolsoPasarela.Rechazado when resultado.Reintentable =>
                Reintentar(reembolso, ahora, resultado.Mensaje ?? "Rechazo transitorio de la pasarela."),

            _ => Fallar(reembolso, resultado.Mensaje ?? "La pasarela rechazó el reintegro."),
        };
    }

    private ResultadoItem Confirmar(Reembolso reembolso, string reembolsoId, DateTime ahora)
    {
        reembolso.MarcarProcesado(reembolsoId, ahora);
        return ResultadoItem.Procesado;
    }

    private ResultadoItem Fallar(Reembolso reembolso, string mensaje)
    {
        reembolso.MarcarFallido(mensaje);
        return ResultadoItem.Fallido;
    }

    private ResultadoItem ReintentarPendiente(
        Reembolso reembolso,
        DateTime ahora,
        ResultadoReembolso resultado)
    {
        if (resultado.ReembolsoId is { Length: > 0 } id)
        {
            reembolso.RegistrarReintegroEnCurso(id);
        }

        return Reintentar(
            reembolso,
            ahora,
            resultado.Mensaje ?? "El reintegro sigue pendiente en la pasarela.");
    }

    private ResultadoItem Reintentar(Reembolso reembolso, DateTime ahora, string mensaje)
    {
        if (reembolso.Intentos >= _opciones.MaximoIntentos)
        {
            return Fallar(
                reembolso,
                $"Se agotaron los {_opciones.MaximoIntentos} intentos. Último error: {mensaje}");
        }

        reembolso.ProgramarReintento(ahora.Add(Espera(reembolso)), mensaje);
        return ResultadoItem.Reintentado;
    }

    /// <summary>
    /// Backoff exponencial configurable más un jitter de hasta 59 segundos
    /// derivado del id, para que un lote de reembolsos no vuelva a la pasarela
    /// todo en el mismo segundo (D-15.4).
    /// </summary>
    private TimeSpan Espera(Reembolso reembolso)
    {
        var indice = Math.Clamp(reembolso.Intentos - 1, 0, _opciones.BackoffMinutos.Length - 1);
        var minutos = _opciones.BackoffMinutos[indice];
        var segundos = Math.Abs(_jitterSegundos(reembolso.Id, reembolso.Intentos)) % 60;

        return TimeSpan.FromMinutes(minutos) + TimeSpan.FromSeconds(segundos);
    }

    private static int JitterDeterminista(Guid id, int intento)
    {
        var bytes = id.ToByteArray();
        return (bytes[0] + bytes[1] + intento * 17) % 60;
    }

    private enum ResultadoItem
    {
        Procesado,
        Reintentado,
        Fallido,
    }
}
