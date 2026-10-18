namespace WashPoints.Infrastructure.Seguridad;

/// <summary>
/// Nombres de los claims del token de sesión (§5.3 de
/// SPECS/slice-05-auth-y-permisos.md). Es un detalle del formato JWT, por eso
/// vive junto al emisor en Infrastructure; la Api también los usa al autorizar.
/// </summary>
public static class ClaimsWashPoints
{
    /// <summary>Id del usuario de la tabla <c>usuarios</c> (D-05.3).</summary>
    public const string Sub = "sub";

    /// <summary>Rol del usuario: <c>cliente</c> | <c>administrador</c>.</summary>
    public const string Rol = "rol";

    /// <summary>Lavadero del administrador; ausente para un cliente.</summary>
    public const string LavaderoId = "lavaderoId";

    /// <summary>Valor del claim <see cref="Rol"/> para un cliente.</summary>
    public const string Cliente = "cliente";

    /// <summary>Valor del claim <see cref="Rol"/> para un administrador.</summary>
    public const string Administrador = "administrador";
}
