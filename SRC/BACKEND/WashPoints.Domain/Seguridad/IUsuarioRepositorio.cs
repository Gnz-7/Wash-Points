namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Puerto de credenciales (SPECS/slice-05 T-51, D-05.3; alta en slice-08).
///
/// No conoce JWT ni hashing: solo entrega el usuario persistido por nombre y
/// agrega credenciales nuevas. El hash lo calcula la capa de aplicación.
/// </summary>
public interface IUsuarioRepositorio
{
    Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken ct);

    /// <summary>
    /// Verifica si el nombre de usuario ya está tomado (D-09.5). La unicidad
    /// real la garantiza el índice `UNIQUE(nombre_usuario)`; esto solo evita
    /// el intento cuando se puede detectar antes.
    /// </summary>
    Task<bool> ExisteAsync(string nombreUsuario, CancellationToken ct);

    /// <summary>
    /// Persiste la credencial dentro de la transacción abierta por
    /// <c>IUnidadDeTrabajo</c> y traduce la violación de unicidad (23505) a
    /// <see cref="UsuarioYaExisteException"/>.
    /// </summary>
    Task AgregarAsync(Usuario usuario, CancellationToken ct);
}
