namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Rechazo de login por credenciales inválidas (D-05.5, CA-9.4 de
/// SPECS/slice-05-auth-y-permisos.md): usuario inexistente, inactivo o
/// contraseña errónea. El mensaje es deliberadamente genérico y nunca distingue
/// el motivo, para no filtrar la existencia de usuarios.
/// </summary>
public sealed class UsuarioNoEncontradoException : Exception
{
    public UsuarioNoEncontradoException(string message) : base(message)
    {
    }
}
