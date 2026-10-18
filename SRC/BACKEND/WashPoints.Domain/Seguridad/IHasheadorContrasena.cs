namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Puerto de hashing de contraseñas (D-05.3, D-05.8). La contraseña en claro
/// nunca se persiste ni se loguea (AGENTS §3.4).
/// </summary>
public interface IHasheadorContrasena
{
    /// <summary>Deriva un hash con salt aleatorio a partir de la contraseña.</summary>
    string Hashear(string contrasena);

    /// <summary>Verifica una contraseña contra un hash almacenado.</summary>
    bool Verificar(string contrasena, string hash);
}
