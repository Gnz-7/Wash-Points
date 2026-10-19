namespace WashPoints.Domain.Metricas;

/// <summary>
/// Lectura agregada del día para el panel de BI (CU-09, US-A08).
/// Contiene tres magnitudes de dinero que son independientes y **nunca se
/// suman** entre sí: la seña es un adelanto y el facturado es el valor del
/// servicio entregado (CA-8.2).
/// </summary>
/// <param name="PuestosTotal">Puestos de lavado configurados en el lavadero.</param>
/// <param name="PuestosActivos">De los anteriores, los que están activos.</param>
/// <param name="PuestosOcupados">Turnos confirmados o en curso que cubren el instante actual.</param>
/// <param name="CalculadoEnUtc">Momento en que se tomó la foto de capacidad.</param>
/// <param name="SenasCobradas">Suma de señas con pago aprobado procesado dentro del día.</param>
/// <param name="FacturadoTotal">Valor de los servicios entregados en el día.</param>
/// <param name="FacturadoMostrador">Subconjunto del anterior cobrado por demanda espontánea.</param>
/// <param name="Atendidos">Turnos finalizados o en curso con inicio en el día.</param>
/// <param name="Espontaneos">De los atendidos, los de origen espontáneo.</param>
/// <param name="NoPresentados">Turnos marcados como no presentados en el día.</param>
/// <param name="Cancelaciones">Turnos cancelados en el día.</param>
public sealed record ResumenMetricas(
    int PuestosTotal,
    int PuestosActivos,
    int PuestosOcupados,
    DateTime CalculadoEnUtc,
    decimal SenasCobradas,
    decimal FacturadoTotal,
    decimal FacturadoMostrador,
    int Atendidos,
    int Espontaneos,
    int NoPresentados,
    int Cancelaciones);
