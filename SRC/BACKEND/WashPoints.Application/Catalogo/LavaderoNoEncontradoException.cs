namespace WashPoints.Application.Catalogo;

/// <summary>
/// El lavadero pedido no existe (SPECS/slice-17, D-17.8).
///
/// El endpoint lo traduce a <c>404 lavadero_no_encontrado</c>. Se lanza antes
/// de escribir cualquier configuración: no se crean franjas ni compatibilidades
/// colgando de un lavadero inexistente.
/// </summary>
public sealed class LavaderoNoEncontradoException : Exception
{
    public LavaderoNoEncontradoException(string mensaje) : base(mensaje)
    {
    }
}
