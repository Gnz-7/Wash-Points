using WashPoints.Domain.Turnos;

namespace WashPoints.Infrastructure.Tiempo;

/// <summary>
/// Única pieza que convierte entre UTC y la hora local del lavadero
/// (ARCHITECTURE §10.6).
///
/// Dos mundos distintos:
/// <list type="bullet">
/// <item>El dominio (<see cref="RangoTurno"/>) vive en <b>UTC</b>.</item>
/// <item>La columna <c>turnos.rango</c> guarda <b>hora local sin zona</b>,
/// porque el EXCLUDE tiene que chocar dos "10:00" aunque se calcularon en
/// franjas UTC distintas. Un <c>tstzrange</c> habría comparado instantes,
/// que es justo lo que no se quiere.</item>
/// </list>
///
/// La zona se resuelve con <see cref="TimeZoneInfo"/>, nunca con aritmética
/// de horas: <c>AddHours(-3)</c> funcionaría hoy —Argentina va en UTC-3 sin
/// cambio de horario desde 2009— pero es un tiro en el pie si el huso se
/// parametriza algún día (§10.6).
/// </summary>
public static class ConversorHorario
{
    /// <summary>
    /// Zona de los lavaderos. Es un dato fijo, no una configuración por
    /// lavadero: ver ARCHITECTURE §10.6 (YAGNI deliberado).
    /// </summary>
    public static readonly TimeZoneInfo ZonaDelLavadero =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    /// <summary>Instante UTC → hora local de la zona del lavadero.</summary>
    public static DateTime ALocal(DateTime instanteUtc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(instanteUtc), ZonaDelLavadero);

    /// <summary>Hora local de la zona del lavadero → instante UTC.</summary>
    public static DateTime AUtc(DateTime horaLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(horaLocal, DateTimeKind.Unspecified), ZonaDelLavadero);

    private static DateTime AsUtc(DateTime instante) =>
        instante.Kind switch
        {
            DateTimeKind.Utc => instante,
            DateTimeKind.Local => instante.ToUniversalTime(),
            _ => DateTime.SpecifyKind(instante, DateTimeKind.Utc),
        };
}
