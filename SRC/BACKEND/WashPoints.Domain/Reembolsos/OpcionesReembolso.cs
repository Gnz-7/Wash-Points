namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Parámetros de la política de reembolso (SPECS/slice-14 D-14.6), leídos de
/// la sección <c>Reembolsos</c> de la configuración.
///
/// Los valores viven acá y no dispersos en condiciones para que el negocio
/// pueda cambiarlos sin tocar código. La validación falla al arrancar, nunca
/// al cancelar: un porcentaje imposible no debe descubrirse con un cliente
/// esperando.
/// </summary>
public sealed class OpcionesReembolso
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Reembolsos";

    /// <summary>Anticipación mínima acordada con producto.</summary>
    public const int AnticipacionMinimaMinutosPorDefecto = 120;

    /// <summary>Porcentaje reintegrable acordado con producto.</summary>
    public const decimal PorcentajeReintegrablePorDefecto = 100m;

    /// <summary>
    /// Anticipación mínima, en minutos, entre la cancelación y el inicio del
    /// turno. Cancelar justo en el límite todavía reintegra.
    /// </summary>
    public int AnticipacionMinimaMinutos { get; set; } =
        AnticipacionMinimaMinutosPorDefecto;

    /// <summary>
    /// Porcentaje de la seña que se reintegra cuando se cancela en plazo.
    /// </summary>
    public decimal PorcentajeReintegrable { get; set; } =
        PorcentajeReintegrablePorDefecto;

    /// <summary>
    /// Valida el rango de los parámetros. La invoca el arranque de la API y el
    /// constructor de <see cref="PoliticaReembolso"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// La anticipación es negativa o el porcentaje cae fuera de [0, 100].
    /// </exception>
    public void Validar()
    {
        if (AnticipacionMinimaMinutos < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AnticipacionMinimaMinutos),
                AnticipacionMinimaMinutos,
                "La anticipación mínima no puede ser negativa.");
        }

        if (PorcentajeReintegrable < 0m || PorcentajeReintegrable > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PorcentajeReintegrable),
                PorcentajeReintegrable,
                "El porcentaje reintegrable debe estar entre 0 y 100.");
        }
    }
}
