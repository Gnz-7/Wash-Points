namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Un turno intentó confirmar un intervalo que otro turno ya ocupa.
///
/// Es la materialización de la restricción EXCLUDE: cuando el motor rechaza el
/// INSERT/UPDATE por solapamiento, la infraestructura traduce la violación de
/// PostgreSQL a esta excepción de dominio. La capa HTTP la mapea a 409.
///
/// Ver ARCHITECTURE §4.2 ("la violación se detecta en el INSERT, no en el
/// COMMIT, de modo que el conflicto se mapea a 409 slot_ya_ocupado").
/// </summary>
public sealed class TurnoConflictoException : Exception
{
    public TurnoConflictoException(string mensaje)
        : base(mensaje)
    {
    }
}