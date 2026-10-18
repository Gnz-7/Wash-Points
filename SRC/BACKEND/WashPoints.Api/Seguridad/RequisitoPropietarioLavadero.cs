using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using WashPoints.Infrastructure.Seguridad;

namespace WashPoints.Api.Seguridad;

/// <summary>
/// Requisito: el administrador de la sesión debe ser dueño del lavadero que
/// aparece como <c>{id}</c> en la ruta (D-05.7).
/// </summary>
public sealed class RequisitoPropietarioLavadero : IAuthorizationRequirement;

/// <summary>
/// Compara el claim <c>lavaderoId</c> con el <c>{id}</c> de la ruta. Si no
/// coinciden, el requisito no se satisface ⇒ 403 <c>no_es_propietario</c>, sin
/// revelar si el lavadero existe (CU-10 A2, CA-9.3).
/// </summary>
public sealed class ManejadorPropietarioLavadero : AuthorizationHandler<RequisitoPropietarioLavadero>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext contexto,
        RequisitoPropietarioLavadero requisito)
    {
        if (contexto.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var propio = contexto.User.FindFirst(ClaimsWashPoints.LavaderoId)?.Value;
        var deLaRuta = http.Request.RouteValues["id"]?.ToString();

        if (Guid.TryParse(propio, out var lavaderoPropio) &&
            Guid.TryParse(deLaRuta, out var lavaderoDeLaRuta) &&
            lavaderoPropio == lavaderoDeLaRuta)
        {
            contexto.Succeed(requisito);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Traduce el rechazo de autorización a un JSON acotado (slice-05, §5.5): 401
/// sin sesión (lo resuelve el handler por defecto) y 403 con
/// <c>rol_insuficiente</c> o <c>no_es_propietario</c> según qué requisito falló.
/// La decisión es del servidor y no filtra la existencia de recursos ajenos.
/// </summary>
public sealed class ManejadorResultadoAutorizacion : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _porDefecto = new();

    public async Task HandleAsync(
        RequestDelegate siguiente,
        HttpContext contexto,
        AuthorizationPolicy politica,
        PolicyAuthorizationResult resultado)
    {
        if (resultado.Succeeded)
        {
            await siguiente(contexto);
            return;
        }

        if (resultado.Forbidden)
        {
            var fallidas = resultado.AuthorizationFailure?.FailedRequirements
                ?? (IEnumerable<IAuthorizationRequirement>)Array.Empty<IAuthorizationRequirement>();

            var falloElRol = fallidas
                .OfType<ClaimsAuthorizationRequirement>()
                .Any(requisito => requisito.ClaimType == ClaimsWashPoints.Rol);

            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            contexto.Response.ContentType = "application/json; charset=utf-8";
            await contexto.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = falloElRol ? Politicas.ErrorRolInsuficiente : Politicas.ErrorNoEsPropietario,
            }));
            return;
        }

        await _porDefecto.HandleAsync(siguiente, contexto, politica, resultado);
    }
}
