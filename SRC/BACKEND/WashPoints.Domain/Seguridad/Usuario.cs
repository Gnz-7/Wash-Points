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

    public Guid Id { get; private set; }

    public string NombreUsuario { get; private set; } = string.Empty;

    /// <summary>Hash PBKDF2 de la contraseña; nunca el claro (AGENTS §3.4).</summary>
    public string HashContrasena { get; private set; } = string.Empty;

    public RolUsuario Rol { get; private set; }

    /// <summary>Lavadero del administrador; nulo para un cliente (D-05.1).</summary>
    public Guid? LavaderoId { get; private set; }

    public bool Activo { get; private set; }

    public DateTime CreadoEn { get; private set; }
}
