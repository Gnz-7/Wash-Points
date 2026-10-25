using WashPoints.Domain.Seguridad;

namespace WashPoints.Application.Registro;

/// <summary>
/// Resultado de un alta exitosa (slice-08, §5.2/§5.3): la cuenta creada, su
/// vínculo (cliente o lavadero) y la sesión ya emitida.
///
/// El token se emite dentro de la transacción, después de persistir: si el alta
/// revierte, no hay sesión que devolver (D-09.2, §4.3).
/// </summary>
public sealed record RegistroSesion(
    Guid UsuarioId,
    Guid? ClienteId,
    Guid? LavaderoId,
    RolUsuario Rol,
    SesionEmitida Sesion);
