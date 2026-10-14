namespace WashPoints.Application.Agenda;

/// <summary>
/// Configuración del ciclo de agenda (sección <c>Agenda</c>). La ventana de
/// llegada es D2 (SPECS/slice-04): tras ese tiempo desde el inicio, un
/// <c>confirmado</c> que no fue canjeado se considera <c>no_presentado</c>.
/// </summary>
public sealed class OpcionesAgenda
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Agenda";

    /// <summary>Minutos de gracia desde el inicio antes de marcar no_presentado.</summary>
    public int VentanaLlegadaMinutos { get; set; } = 15;

    /// <summary>Segundos entre ticks del worker de cierre de agenda.</summary>
    public int IntervaloWorkerSegundos { get; set; } = 60;
}
