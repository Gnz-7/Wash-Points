namespace WashPoints.Domain.Catalogo;

/// <summary>
/// No existe un puesto con ese id en el lavadero indicado (o pertenece a otro
/// lavadero: indistinguible a propósito, D-04.7). La capa HTTP lo mapea a 404
/// <c>puesto_no_encontrado</c> (SPECS/slice-04, CA-S4.16).
/// </summary>
public sealed class PuestoNoEncontradoException : Exception
{
    public PuestoNoEncontradoException(string mensaje) : base(mensaje)
    {
    }
}
