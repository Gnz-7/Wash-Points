namespace WashPoints.Application.Seguridad;

/// <summary>
/// Credenciales de login (CU-10, §5.1 de SPECS/slice-05-auth-y-permisos.md).
/// </summary>
public sealed record SolicitudLogin(string NombreUsuario, string Contrasena);
