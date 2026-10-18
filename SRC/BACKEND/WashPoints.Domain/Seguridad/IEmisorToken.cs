namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Puerto de emisión de sesiones (D-05.3). Es agnóstico del formato del token:
/// la implementación concreta (JWT, HS256) vive en Infrastructure.
/// </summary>
public interface IEmisorToken
{
    /// <summary>
    /// Emite una sesión firmada para el usuario indicado. <paramref name="lavaderoId"/>
    /// solo aplica a administradores; <paramref name="clienteId"/> solo viaja si
    /// la cuenta tiene un cliente de negocio vinculado (slice-05b, D-06.2).
    /// </summary>
    SesionEmitida Emitir(
        Guid usuarioId,
        RolUsuario rol,
        Guid? lavaderoId,
        Guid? clienteId = null);
}
