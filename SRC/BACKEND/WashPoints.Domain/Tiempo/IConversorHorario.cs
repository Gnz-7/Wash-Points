namespace WashPoints.Domain.Tiempo;

/// <summary>
/// Conversión entre UTC y la hora local del lavadero (SPECS/slice-17, D-17.5).
///
/// Las franjas de atención y la grilla de slots se expresan en hora local,
/// mientras que los turnos se persisten en UTC. Application no puede referenciar
/// Infrastructure, así que la conversión entra por este puerto y el adaptador
/// delega en el conversor único del proyecto. Sin esto, la reserva compararía
/// horas UTC contra franjas locales y rechazaría turnos válidos.
/// </summary>
public interface IConversorHorario
{
    /// <summary>Instante UTC → hora local del lavadero.</summary>
    DateTime ALocal(DateTime instanteUtc);

    /// <summary>Hora local del lavadero → instante UTC.</summary>
    DateTime AUtc(DateTime horaLocal);
}
