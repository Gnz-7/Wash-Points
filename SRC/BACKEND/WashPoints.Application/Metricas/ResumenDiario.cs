namespace WashPoints.Application.Metricas;

/// <summary>
/// Resumen diario que consume el panel de administración (CU-09, US-A08).
/// </summary>
public sealed record ResumenDiario(
    DateOnly Fecha,
    CapacidadInstalada Capacidad,
    IngresosDelDia Ingresos,
    ConteoTurnos Turnos);

/// <summary>
/// Foto del presente: puestos configurados, activos y ocupados ahora.
/// El <c>?fecha=</c> del resumen no altera la capacidad, solo los ingresos y
/// los conteos del día.
/// </summary>
public sealed record CapacidadInstalada(
    int PuestosTotal,
    int PuestosActivos,
    int PuestosOcupados,
    DateTime CalculadoEnUtc);

/// <summary>
/// Tres magnitudes independientes que no se suman entre sí (CA-8.2): la seña es
/// un adelanto y el facturado, el valor del servicio entregado. No existe un
/// campo de "ingresos totales" a propósito.
/// </summary>
public sealed record IngresosDelDia(
    decimal SenasCobradas,
    decimal FacturadoTotal,
    decimal FacturadoMostrador,
    string Moneda);

/// <summary>
/// Conteos del día. Son conteos, no tasas: CU-09 no define denominadores.
/// </summary>
public sealed record ConteoTurnos(
    int Atendidos,
    int Espontaneos,
    int NoPresentados,
    int Cancelaciones);
