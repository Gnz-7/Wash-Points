namespace WashPoints.Domain.Seguridad;

/// <summary>
/// El nombre de usuario ya está tomado (slice-08, D-09.5).
///
/// Lo produce el repositorio al traducir la violación del índice
/// `UNIQUE(nombre_usuario)`; el endpoint lo mapea a
/// `409 { error: "usuario_ya_existe" }`.
/// </summary>
public sealed class UsuarioYaExisteException : Exception
{
    public UsuarioYaExisteException(string message) : base(message)
    {
    }

    public UsuarioYaExisteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
