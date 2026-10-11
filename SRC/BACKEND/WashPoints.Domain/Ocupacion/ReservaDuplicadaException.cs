namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// El INSERT de una reserva violó la unicidad de <c>idempotency_key</c>
/// (SQLSTATE 23505). Es uno de los dos caminos de un reintento idéntico: la
/// fila original ya salió del predicado parcial del EXCLUDE (p. ej. cancelada o
/// expirada), así que la unicidad dispara antes que la exclusión.
///
/// La reserva lo captura y reconsulta por clave (SPECS/slice-01 T-14).
/// </summary>
public sealed class ReservaDuplicadaException : Exception
{
    public ReservaDuplicadaException(string mensaje) : base(mensaje)
    {
    }

    public ReservaDuplicadaException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}
