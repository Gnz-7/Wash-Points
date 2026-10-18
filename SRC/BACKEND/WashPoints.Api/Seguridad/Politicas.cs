namespace WashPoints.Api.Seguridad;

/// <summary>
/// Nombres de política y códigos de error de autorización (slice-05, §5.2 y §5.5).
/// </summary>
public static class Politicas
{
    /// <summary>Endpoints de administración: exige rol <c>administrador</c>.</summary>
    public const string Administrador = "Administrador";

    /// <summary>
    /// Endpoints de administración con <c>{id}</c> de lavadero en la ruta: exige
    /// además que el administrador sea dueño de ese lavadero (D-05.7).
    /// </summary>
    public const string PropietarioDelLavadero = "PropietarioDelLavadero";

    /// <summary>Error 403: el rol de la sesión no alcanza para el endpoint.</summary>
    public const string ErrorRolInsuficiente = "rol_insuficiente";

    /// <summary>Error 403: el administrador no es dueño del lavadero de la ruta.</summary>
    public const string ErrorNoEsPropietario = "no_es_propietario";
}
