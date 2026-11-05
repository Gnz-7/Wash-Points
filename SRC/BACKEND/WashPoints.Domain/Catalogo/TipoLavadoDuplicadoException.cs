namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Ya existe un tipo de lavado con ese nombre en el lavadero (SPECS/slice-13
/// D-13.10). Lo detecta el índice único <c>(lavadero_id, nombre)</c> y el
/// repositorio lo traduce. La API responde 409 <c>tipo_ya_existe</c>.
/// </summary>
public sealed class TipoLavadoDuplicadoException : Exception
{
    public TipoLavadoDuplicadoException(string message)
        : base(message)
    {
    }

    public TipoLavadoDuplicadoException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
