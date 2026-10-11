namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Ejecuta un bloque de trabajo dentro de una transacción de base de datos.
///
/// Existe para la reserva (SPECS/slice-01 T-13, CA-S1.3): el turno se inserta,
/// luego se pide la preferencia a la pasarela y, si la pasarela falla, la
/// transacción se revierte y no queda ni turno huérfano ni preferencia sin
/// turno. Si el <paramref name="trabajo"/> lanza, la transacción se revierte.
/// </summary>
public interface IUnidadDeTrabajo
{
    Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> trabajo,
        CancellationToken ct);
}
