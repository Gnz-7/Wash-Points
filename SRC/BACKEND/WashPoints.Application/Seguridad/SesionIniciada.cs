using WashPoints.Domain.Seguridad;

namespace WashPoints.Application.Seguridad;

/// <summary>
/// Resultado de un login exitoso (T-53): la sesión firmada más el rol y el
/// lavadero del usuario, que el endpoint compone en la respuesta HTTP (§5.1).
/// </summary>
public sealed record SesionIniciada(SesionEmitida Sesion, RolUsuario Rol, Guid? LavaderoId);
