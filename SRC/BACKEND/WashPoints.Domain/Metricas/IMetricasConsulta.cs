namespace WashPoints.Domain.Metricas;

/// <summary>
/// Puerto de solo lectura del módulo de métricas (CU-09). El dominio declara
/// qué información agregada necesita el panel de BI; la implementación vive en
/// Infrastructure (ARCHITECTURE §2.2).
/// Ninguna operación de este puerto modifica el modelo operativo: no cambia
/// estados, no escribe filas ni encola eventos en el outbox (CA-S6.7).
/// </summary>
public interface IMetricasConsulta
{
    /// <summary>
    /// Resumen del día local del lavadero: capacidad instalada, tres magnitudes
    /// de dinero independientes (señas, facturado total y facturado de
    /// mostrador) y conteos de turnos. Un día sin actividad devuelve ceros.
    /// </summary>
    Task<ResumenMetricas> ObtenerResumenAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Minutos ocupados por puesto dentro de la ventana indicada. Se listan
    /// todos los puestos del lavadero (los de cero minutos incluidos); si se
    /// pide un <paramref name="puestoId"/> que no pertenece al lavadero, la
    /// lista vuelve vacía.
    /// </summary>
    Task<IReadOnlyList<OcupacionPuesto>> ObtenerOcupacionAsync(
        Guid lavaderoId,
        Guid? puestoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct = default);
}
