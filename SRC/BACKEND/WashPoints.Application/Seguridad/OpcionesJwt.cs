namespace WashPoints.Application.Seguridad;

/// <summary>
/// Configuración de emisión y validación de sesiones JWT (sección <c>Jwt</c>
/// de la configuración; D-05.2 de SPECS/slice-05-auth-y-permisos.md).
///
/// La clave de firma vive exclusivamente en configuración/entorno y nunca se
/// commitea (AGENTS §3.4).
/// </summary>
public sealed class OpcionesJwt
{
    /// <summary>Clave simétrica para HS256. Debe tener al menos 32 caracteres.</summary>
    public string ClaveSecreta { get; set; } = string.Empty;

    /// <summary>Emisor (<c>iss</c>) que se graba y se exige al validar.</summary>
    public string Emisor { get; set; } = "washpoints";

    /// <summary>Audiencia (<c>aud</c>) que se graba y se exige al validar.</summary>
    public string Audiencia { get; set; } = "washpoints-app";

    /// <summary>Vigencia de la sesión en minutos (D-05.2; por defecto 60).</summary>
    public int MinutosVida { get; set; } = 60;
}
