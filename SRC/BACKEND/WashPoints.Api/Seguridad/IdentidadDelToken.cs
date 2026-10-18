using System.Security.Claims;
using WashPoints.Domain.Seguridad;
using WashPoints.Infrastructure.Seguridad;

namespace WashPoints.Api.Seguridad;

/// <summary>
/// Traduce la sesión ya validada por <c>AddJwtBearer</c> a la identidad de
/// dominio (D-06.2). Es el único punto de la Api que conoce los nombres de
/// claim: el resto del código trabaja con <see cref="IdentidadUsuario"/>.
/// </summary>
public static class IdentidadDelToken
{
    /// <summary>
    /// Devuelve la identidad del portador, o <c>null</c> si no hay sesión
    /// válida o el claim <c>sub</c> no es un identificador.
    /// </summary>
    public static IdentidadUsuario? De(ClaimsPrincipal? usuario)
    {
        if (usuario?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (!Guid.TryParse(usuario.FindFirst(ClaimsWashPoints.Sub)?.Value, out var usuarioId))
        {
            return null;
        }

        var rol = usuario.FindFirst(ClaimsWashPoints.Rol)?.Value == ClaimsWashPoints.Administrador
            ? RolUsuario.Administrador
            : RolUsuario.Cliente;

        return new IdentidadUsuario(
            usuarioId,
            rol,
            LeerGuid(usuario, ClaimsWashPoints.LavaderoId),
            LeerGuid(usuario, ClaimsWashPoints.ClienteId));
    }

    private static Guid? LeerGuid(ClaimsPrincipal usuario, string tipo) =>
        Guid.TryParse(usuario.FindFirst(tipo)?.Value, out var id) ? id : null;
}
