namespace WashPoints.Infrastructure.Pagos;

/// <summary>Tipo de evento notificado por la pasarela (IPN de Mercado Pago).</summary>
public enum TipoEventoPago
{
    /// <summary>Notificación de un pago (evento `payment`).</summary>
    Payment,

    /// <summary>Notificación de una suscripción/plan (evento `plan`).</summary>
    Plan,
}

/// <summary>
/// Estado del pago según la pasarela. Se persiste tal cual llega (texto
/// snake_case) para conciliación: no es un juicio de la app sobre la seña.
/// </summary>
public enum ResultadoPago
{
    /// <summary>approved — cobrado, habilita la confirmación.</summary>
    Approved,

    /// <summary>pending — aún no resuelto; no confirma ni cancela.</summary>
    Pending,

    /// <summary>rejected/cancelled — no cobrado; el turno se cancela.</summary>
    Rejected,

    /// <summary>in_process — en curso; no confirma.</summary>
    InProcess,

    /// <summary>charged_back — disputa; deja el turno como estaba.</summary>
    ChargedBack,
}

/// <summary>
/// Evento de pago persistido. Es la fuente evaluable de RB-1 (que la seña se
/// pagó) y la idempotencia del webhook: la fila es única por
/// <c>mp_payment_id</c> y por turno.
///
/// No es un agregado del dominio de negocio: pertenece a la integración con la
/// pasarela (SPECS/slice-01 §4), por eso vive en Infrastructure.
/// </summary>
public sealed class Pago
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Pago()
    {
    }

    private Pago(
        Guid id,
        Guid turnoId,
        string mpPaymentId,
        TipoEventoPago tipoEvento,
        ResultadoPago resultado,
        DateTime procesadoEn)
    {
        Id = id;
        TurnoId = turnoId;
        MpPaymentId = mpPaymentId;
        TipoEvento = tipoEvento;
        Resultado = resultado;
        ProcesadoEn = procesadoEn;
    }

    public Guid Id { get; private set; }
    public Guid TurnoId { get; private set; }
    public string MpPaymentId { get; private set; } = string.Empty;
    public TipoEventoPago TipoEvento { get; private set; }
    public ResultadoPago Resultado { get; private set; }
    public DateTime ProcesadoEn { get; private set; }

    public static Pago Registrar(
        Guid turnoId,
        string mpPaymentId,
        TipoEventoPago tipoEvento,
        ResultadoPago resultado)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);

        if (tipoEvento is not TipoEventoPago.Payment and not TipoEventoPago.Plan)
        {
            throw new ArgumentOutOfRangeException(nameof(tipoEvento));
        }

        if (!Enum.IsDefined(resultado))
        {
            throw new ArgumentOutOfRangeException(nameof(resultado));
        }

        return new Pago(
            Guid.NewGuid(), turnoId, mpPaymentId.Trim(), tipoEvento, resultado, DateTime.UtcNow);
    }
}