namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Puerto de lectura de credenciales (SPECS/slice-05 T-51, D-05.3).
///
/// No conoce JWT ni hashing: solo entrega el usuario persistido por nombre,
/// para que la capa de aplicación verifique la contraseña y emita la sesión.
/// </summary>
public interface IUsuarioRepositorio
{
    Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken ct);
}
