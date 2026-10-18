namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Puerto de validación de sesiones (D-05.3): verifica firma, emisor, audiencia
/// y vigencia, y devuelve la identidad del usuario o <c>null</c> si el token no
/// es válido. La implementación concreta vive fuera del dominio.
/// </summary>
public interface IValidadorToken
{
    IdentidadUsuario? Validar(string token);
}
