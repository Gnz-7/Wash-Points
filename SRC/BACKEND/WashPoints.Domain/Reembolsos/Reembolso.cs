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
}
