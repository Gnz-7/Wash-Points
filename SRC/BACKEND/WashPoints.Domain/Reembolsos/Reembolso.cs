using System;

namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Reintegro de la seña de un turno cancelado por el cliente
/// (SPECS/slice-14 §5.2, D-14.7).
///
/// Es una fila por turno: registra qué se decidió (<see cref="Estado"/>), por
/// qué (<see cref="Motivo"/>) y cuánto (<see cref="Monto"/>), con el monto
/// congelado al momento de cancelar. La ejecución efectiva del reintegro contra
/// la pasarela queda fuera de este slice (D-14.8): por eso el estado nace
/// <see cref="EstadoReembolso.Pendiente"/> y <see cref="ProcesadoEn"/> queda en
/// <c>null</c> hasta que un worker de pagos lo cierre.
///
/// Un turno que se cancela sin pago (o fuera de plazo) también deja fila, en
/// estado <see cref="EstadoReembolso.NoAplica"/>: el histórico debe poder
/// explicar por qué no se reintegró nada.
/// </summary>
public sealed class Reembolso
{
    /// <summary>Constructor para EF Core.</summary>
    public Reembolso()
    {
    }

    private Reembolso(
        Guid id,
        Guid turnoId,
        EstadoReembolso estado,
        MotivoReembolso motivo,
        decimal monto,
        DateTime creadoEn)
    {
        Id = id;
        TurnoId = turnoId;
        Estado = estado;
        Motivo = motivo;
        Monto = monto;
        CreadoEn = creadoEn;
    }

    public Guid Id { get; private set; }

    /// <summary>Turno que origina el reembolso. Único: un turno se cancela una vez.</summary>
    public Guid TurnoId { get; private set; }

    public EstadoReembolso Estado { get; private set; }

    public MotivoReembolso Motivo { get; private set; }

    /// <summary>Monto a reintegrar, en la moneda de la seña. Cero cuando no aplica.</summary>
    public decimal Monto { get; private set; }

    public DateTime CreadoEn { get; private set; }

    /// <summary>Cuándo lo ejecutó la pasarela. <c>null</c> mientras siga pendiente.</summary>
    public DateTime? ProcesadoEn { get; private set; }

    /// <summary>Intentos de ejecución ya iniciados por el worker (slice-15 D-15.4).</summary>
    public int Intentos { get; private set; }

    /// <summary>Cuándo vuelve a estar disponible para el worker. <c>null</c> = ya.</summary>
    public DateTime? ProximoIntentoEn { get; private set; }

    /// <summary>Lease del reclamo atómico: mientras esté vigente, otro worker no lo toma.</summary>
    public DateTime? ReclamadoEn { get; private set; }

    /// <summary>Último error registrado por el worker, para diagnóstico. Sin PII.</summary>
    public string? UltimoError { get; private set; }

    /// <summary>Identificador del reintegro en la pasarela, para reconciliación idempotente.</summary>
    public string? MpRefundId { get; private set; }

    /// <summary>
    /// Registra el resultado de la política de reembolso para un turno que se
    /// acaba de cancelar. No llama a la pasarela: sólo deja la decisión
    /// asentada para que el slice de pagos la ejecute (D-14.7, D-14.8).
    /// </summary>
    /// <param name="turnoId">Turno cancelado. No puede ser <see cref="Guid.Empty"/>.</param>
    /// <param name="estado">Estado inicial del reembolso.</param>
    /// <param name="motivo">Motivo que fijó la política.</param>
    /// <param name="monto">Monto a reintegrar. No puede ser negativo.</param>
    /// <param name="creadoEn">Instante de la decisión, en UTC.</param>
    /// <exception cref="ArgumentException">El turno es <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">El monto es negativo.</exception>
    public static Reembolso Registrar(
        Guid turnoId,
        EstadoReembolso estado,
        MotivoReembolso motivo,
        decimal monto,
        DateTime creadoEn)
    {
        if (turnoId == Guid.Empty)
        {
            throw new ArgumentException(
                "El reembolso necesita el turno que lo origina.",
                nameof(turnoId));
        }

        if (monto < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(monto),
                monto,
                "El monto del reembolso no puede ser negativo.");
        }

        return new Reembolso(Guid.NewGuid(), turnoId, estado, motivo, monto, creadoEn);
    }

    /// <summary>
    /// Marca el reembolso como tomado por un worker: suma un intento y fija el
    /// lease. Lo hace el repositorio en la misma sentencia atómica que elige la
    /// fila, así que dos workers no procesan el mismo reembolso (D-15.4).
    /// </summary>
    public void Reclamar(DateTime ahoraUtc)
    {
        Intentos++;
        ReclamadoEn = ahoraUtc;
    }

    /// <summary>
    /// Deja el reembolso pendiente para un próximo intento. No incrementa
    /// <see cref="Intentos"/>: el intento ya se contó al reclamarlo.
    /// </summary>
    public void ProgramarReintento(DateTime proximoIntentoUtc, string? error)
    {
        ProximoIntentoEn = proximoIntentoUtc;
        UltimoError = error;
        ReclamadoEn = null;
    }

    /// <summary>
    /// La pasarela confirmó el reintegro: el reembolso queda cerrado.
    /// </summary>
    /// <exception cref="ArgumentException">El id del reintegro está vacío.</exception>
    public void MarcarProcesado(string mpRefundId, DateTime procesadoEnUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpRefundId);

        MpRefundId = mpRefundId.Trim();
        Estado = EstadoReembolso.Procesado;
        ProcesadoEn = procesadoEnUtc;
        ProximoIntentoEn = null;
        ReclamadoEn = null;
        UltimoError = null;
    }

    /// <summary>
    /// La pasarela aceptó el reintegro pero todavía no lo confirmó. Se guarda su
    /// id para reconciliar en el próximo tick en vez de pedirlo de nuevo
    /// (D-15.5).
    /// </summary>
    public void RegistrarReintegroEnCurso(string mpRefundId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpRefundId);
        MpRefundId = mpRefundId.Trim();
    }

    /// <summary>Rechazo definitivo o intentos agotados: no se vuelve a intentar.</summary>
    public void MarcarFallido(string? error)
    {
        Estado = EstadoReembolso.Fallido;
        UltimoError = error;
        ProximoIntentoEn = null;
        ReclamadoEn = null;
        ProcesadoEn = null;
    }

    /// <summary>
    /// El webhook avisó de un movimiento del reintegro: adelanta el próximo
    /// intento y libera el lease para que el worker lo reconcilie ya
    /// (D-15.6). No toca <see cref="Monto"/> ni <see cref="Estado"/>: la
    /// verdad la fija la consulta a la pasarela, nunca el payload.
    /// </summary>
    public void SenalarVencimiento(DateTime ahoraUtc)
    {
        if (Estado != EstadoReembolso.Pendiente)
        {
            return;
        }

        ProximoIntentoEn = ahoraUtc;
        ReclamadoEn = null;
    }
}
