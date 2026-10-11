namespace WashPoints.Application.Pagos;

/// <summary>
/// Configuración del ciclo de vida de la seña (sección <c>Pagos</c> de la
/// configuración). Valores por defecto aprobados en SPECS/slice-01 §3.
/// </summary>
public sealed class OpcionesPago
{
    /// <summary>Minutos que un <c>pendiente_pago</c> bloquea el intervalo antes de expirar (TTL).</summary>
    public int TtlReservaMinutos { get; set; } = 5;

    /// <summary>
    /// Minutos tras la creación tras los que el worker consulta a la pasarela
    /// si el webhook no llegó. Debe ser menor que <see cref="TtlReservaMinutos"/>.
    /// </summary>
    public int VentanaRespaldoMinutos { get; set; } = 3;

    /// <summary>Segundos entre ticks del worker de respaldo y expiración.</summary>
    public int IntervaloWorkerSegundos { get; set; } = 30;

    /// <summary>
    /// Segundos máximos que el worker espera una respuesta de la pasarela
    /// durante el respaldo. Acota la llamada de red para que un proveedor lento
    /// no retrase el ciclo de expiración local (T-21).
    /// </summary>
    public int TimeoutRespaldoSegundos { get; set; } = 10;
}
