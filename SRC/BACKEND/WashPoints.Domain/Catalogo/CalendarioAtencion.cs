namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Regla pura de horario de atención (SPECS/slice-17, D-17.4, R-1).
///
/// Decide si un rango local de turno entra <b>completo</b> en una franja activa
/// del mismo día. No conoce EF, ni la base, ni el reloj: es una función de las
/// franjas y del rango, y por eso se puede cubrir con casos de borde sin
/// levantar Postgres.
///
/// El cliente nunca reimplementa esta regla (D-17.12): el servidor es la única
/// fuente de verdad.
/// </summary>
public static class CalendarioAtencion
{
    /// <summary>
    /// <c>true</c> sólo si existe una franja activa del día de
    /// <paramref name="inicioLocal"/> que contiene por completo a
    /// [<paramref name="inicioLocal"/>, <paramref name="finLocal"/>).
    /// </summary>
    /// <remarks>
    /// Un rango vacío, invertido o que termina al día siguiente se rechaza:
    /// las franjas no cruzan la medianoche (D-17.3).
    /// </remarks>
    public static bool EstaAbierto(
        IReadOnlyList<FranjaAtencion> franjas,
        DateTime inicioLocal,
        DateTime finLocal)
    {
        ArgumentNullException.ThrowIfNull(franjas);

        if (finLocal <= inicioLocal)
        {
            return false;
        }

        // El turno no puede partirse entre dos días: una franja vive en un día.
        if (finLocal.Date != inicioLocal.Date)
        {
            return false;
        }

        var dia = DiaIso(inicioLocal);

        foreach (var franja in franjas)
        {
            if (!franja.Activo || franja.DiaSemana != dia)
            {
                continue;
            }

            if (franja.Apertura <= TimeOnly.FromDateTime(inicioLocal)
                && TimeOnly.FromDateTime(finLocal) <= franja.Cierre)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Franjas activas del día de <paramref name="fecha"/>, ordenadas por
    /// apertura. Es la base de la grilla de slots (D-17.9).
    /// </summary>
    public static IReadOnlyList<FranjaAtencion> FranjasDelDia(
        IReadOnlyList<FranjaAtencion> franjas,
        DateTime fecha)
    {
        ArgumentNullException.ThrowIfNull(franjas);

        var dia = DiaIso(fecha);

        return franjas
            .Where(franja => franja.Activo && franja.DiaSemana == dia)
            .OrderBy(franja => franja.Apertura)
            .ToList();
    }

    /// <summary>Día de la semana ISO-8601 (1 = lunes … 7 = domingo).</summary>
    private static int DiaIso(DateTime fecha)
    {
        var dia = (int)fecha.DayOfWeek;

        // DayOfWeek usa 0 = domingo; ISO usa 7.
        return dia == 0 ? 7 : dia;
    }
}
