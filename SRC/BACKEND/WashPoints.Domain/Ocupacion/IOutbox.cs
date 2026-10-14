namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Puerto de escritura de la outbox (RB-2, ARCHITECTURE §7.3).
///
/// <para>
/// <b>Contrato (decisión D12):</b> <see cref="Encolar"/> solo agrega la fila al
/// Change Tracker del <c>DbContext</c> compartido; <b>nunca</b> dispara un
/// <c>SaveChangesAsync</c> propio. El único commit lo hace la unidad de trabajo
/// que envuelve la operación, de modo que el cambio de ocupación y su evento se
/// persisten atómicamente.
/// </para>
/// </summary>
public interface IOutbox
{
    void Encolar(EventoOcupacion evento);

    void Encolar(EventoAgenda evento);
}
