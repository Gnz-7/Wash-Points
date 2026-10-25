namespace WashPoints.Application.Registro;

/// <summary>
/// El nombre de usuario no sirve como identificador (slice-08, §5.4): vacío,
/// con espacios o fuera de la longitud admitida.
/// </summary>
public sealed class UsuarioInvalidoException : Exception
{
    public UsuarioInvalidoException(string message) : base(message)
    {
    }
}
