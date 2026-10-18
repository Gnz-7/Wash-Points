namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Perfil de acceso de una credencial (CU-10, D-05.1 de
/// SPECS/slice-05-auth-y-permisos.md). El rol decide qué endpoints quedan
/// habilitados; la autorización se resuelve siempre en el servidor
/// (ARCHITECTURE §7.1).
/// </summary>
public enum RolUsuario
{
    Cliente,
    Administrador,
}
