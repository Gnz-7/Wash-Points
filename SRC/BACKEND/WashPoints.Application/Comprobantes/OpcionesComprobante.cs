namespace WashPoints.Application.Comprobantes;

/// <summary>
/// Configuración del comprobante (sección <c>Comprobantes</c> de la
/// configuración). Valores por defecto aprobados en SPECS/slice-02 §3 (D2).
/// </summary>
public sealed class OpcionesComprobante
{
    /// <summary>
    /// Horas de vigencia del comprobante desde la confirmación del turno.
    /// <c>expira_en = confirmadoEn + TtlVigenciaHoras</c>.
    /// </summary>
    public int TtlVigenciaHoras { get; set; } = 24;

    /// <summary>
    /// Clave AES-256 en base64 para cifrar el token en reposo. Va por variable
    /// de entorno (<c>Comprobantes:ClaveCifrado</c>), nunca en el repositorio
    /// (AGENTS §3.4). Si falta, la emisión falla de forma explícita.
    /// </summary>
    public string? ClaveCifrado { get; set; }
}
