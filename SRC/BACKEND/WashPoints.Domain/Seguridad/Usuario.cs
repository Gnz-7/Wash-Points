namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Credencial de acceso al sistema (CU-10, D-05.1 de
/// SPECS/slice-05-auth-y-permisos.md).
///
/// El dominio conoce la identidad y el rol del usuario, nunca el detalle del
/// token ni del hashing: eso vive en Application/Infrastructure (D-05.3).
///
/// <see cref="LavaderoId"/> es nulo por diseño para un
/// <see cref="RolUsuario.Cliente"/>; un <see cref="RolUsuario.Administrador"/>
/// siempre pertenece a un lavadero. La regla se materializa como CHECK en la
/// tabla `usuarios` (D-05.1).
/// </summary>
public sealed class Usuario
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Usuario()
    {
    }

    /// <summary>
    /// Alta de una credencial nueva (slice-08, D-09.2/D-09.3). El hash llega
    /// ya calculado: el dominio nunca ve la contraseña en claro.
    ///
    /// Las invariantes de rol son las mismas que el CHECK de la tabla: un
    /// administrador pertenece a un lavadero y no a un cliente de negocio; un
    /// cliente de negocio no pertenece a un lavadero.
    /// </summary>
    public static Usuario Crear(
        string nombreUsuario,
        string hashContrasena,
        RolUsuario rol,
        Guid? lavaderoId = null,
        Guid? clienteId = null)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario))
        {
            throw new ArgumentException(
                "El nombre de usuario es obligatorio.", nameof(nombreUsuario));
        }

        if (string.IsNullOrWhiteSpace(hashContrasena))
        {
            throw new ArgumentException(
                "El hash de la contraseña es obligatorio.", nameof(hashContrasena));
        }

        if (rol == RolUsuario.Administrador && lavaderoId is null)
        {
            throw new ArgumentException(
                "Un administrador debe pertenecer a un lavadero.", nameof(lavaderoId));
        }

        if (rol == RolUsuario.Administrador && clienteId is not null)
        {
            throw new ArgumentException(
                "Un administrador no se vincula a un cliente de negocio.",
                nameof(clienteId));
        }

        if (rol == RolUsuario.Cliente && clienteId is null)
        {
            throw new ArgumentException(
                "Un cliente debe estar vinculado a un cliente de negocio.",
                nameof(clienteId));
        }

        if (rol == RolUsuario.Cliente && lavaderoId is not null)
        {
            throw new ArgumentException(
                "Un cliente no pertenece a un lavadero.", nameof(lavaderoId));
        }

        return new Usuario
        {
            Id = Guid.NewGuid(),
            NombreUsuario = nombreUsuario.Trim(),
            HashContrasena = hashContrasena,
            Rol = rol,
            LavaderoId = lavaderoId,
            ClienteId = clienteId,
            Activo = true,
            CreadoEn = DateTime.UtcNow,
        };
    }

    public Guid Id { get; private set; }

    public string NombreUsuario { get; private set; } = string.Empty;

    /// <summary>Hash PBKDF2 de la contraseña; nunca el claro (AGENTS §3.4).</summary>
    public string HashContrasena { get; private set; } = string.Empty;

    public RolUsuario Rol { get; private set; }

    /// <summary>Lavadero del administrador; nulo para un cliente (D-05.1).</summary>
    public Guid? LavaderoId { get; private set; }

    /// <summary>
    /// Cliente de negocio vinculado a la cuenta (slice-05b, D-06.1). Nulo
    /// mientras no exista vinculación: sin vínculo no se accede a comprobantes.
    /// </summary>
    public Guid? ClienteId { get; private set; }

    public bool Activo { get; private set; }

    public DateTime CreadoEn { get; private set; }
}
