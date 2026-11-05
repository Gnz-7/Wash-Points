namespace WashPoints.Domain.Catalogo;

/// <summary>
/// No existe el tipo de lavado pedido dentro del lavadero del administrador
/// (SPECS/slice-13 D-13.9). La API lo traduce a 404 <c>tipo_no_encontrado</c>.
///
/// Un tipo que pertenece a otro lavadero también cae acá: no se revela su
/// existencia.
/// </summary>
public sealed class TipoLavadoNoEncontradoException : Exception
{
    public TipoLavadoNoEncontradoException(string message)
        : base(message)
    {
    }
}
