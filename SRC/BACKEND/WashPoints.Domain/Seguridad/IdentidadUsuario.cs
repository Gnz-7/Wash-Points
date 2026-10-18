namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Identidad resuelta a partir de una sesión válida (D-05.3). Agnóstica del
/// formato del token (JWT u otro).
/// </summary>
public sealed record IdentidadUsuario(
    Guid UsuarioId,
    RolUsuario Rol,
    Guid? LavaderoId,
    Guid? ClienteId);
