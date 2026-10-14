namespace WashPoints.Domain.Catalogo;

/// <summary>
/// El INSERT/UPDATE de un puesto violó la unicidad del índice
/// <c>(lavadero_id, nombre)</c> (SQLSTATE 23505). La capa HTTP lo mapea a 409
/// <c>puesto_ya_existe</c> (SPECS/slice-04, CA-S4.5).
/// </summary>
public sealed class PuestoDuplicadoException : Exception
{
    public PuestoDuplicadoException(string mensaje) : base(mensaje)
    {
    }

    public PuestoDuplicadoException(string mensaje, Exception interna)
        : base(mensaje, interna)
    {
    }
}
