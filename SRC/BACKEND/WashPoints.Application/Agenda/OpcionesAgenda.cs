namespace WashPoints.Application.Agenda;

/// <summary>
/// Configuración del ciclo de agenda (sección <c>Agenda</c>). La ventana de
/// llegada es D2 (SPECS/slice-04): tras ese tiempo desde el inicio, un
/// <c>confirmado</c> que no fue canjeado se considera <c>no_presentado</c>.
///
/// Desde el slice-17 la misma sección gobierna las reglas temporales de la
/// reserva (D-17.5, R-2). No confundir <see cref="AnticipacionMinimaMinutos"/>
/// con <c>OpcionesPago.TtlReservaMinutos</c>: aquél es cuánto falta como mínimo
/// para reservar; éste, cuánto vive un <c>pendiente_pago</c> antes de expirar.
/// Coinciden en 5 minutos por defecto, pero se configuran por separado.
/// </summary>
public sealed class OpcionesAgenda
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Agenda";

    /// <summary>Minutos de gracia desde el inicio antes de marcar no_presentado.</summary>
    public int VentanaLlegadaMinutos { get; set; } = 15;

    /// <summary>Segundos entre ticks del worker de cierre de agenda.</summary>
    public int IntervaloWorkerSegundos { get; set; } = 60;

    /// <summary>
    /// Antelación mínima, en minutos, entre el inicio del turno y el momento de
    /// reservar. Evita reservar un turno que empieza en los próximos 5 minutos.
    /// </summary>
    public int AnticipacionMinimaMinutos { get; set; } = 5;

    /// <summary>
    /// Horizonte máximo, en días, hasta el que se puede reservar.
    /// </summary>
    public int HorizonteMaximoDias { get; set; } = 7;
}
