namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Franja de atención semanal de un lavadero (SPECS/slice-17, D-17.1, R-1).
///
/// Una franja declara que el lavadero atiende entre <see cref="Apertura"/> y
/// <see cref="Cierre"/> de un día de la semana. Puede haber varias por día
/// (mañana y tarde) y un día sin franjas significa cerrado (D-17.2).
///
/// Las horas son de la hora local del lavadero y se guardan como <c>time</c>
/// sin zona: son regla de calendario, no un instante. Una franja nunca cruza
/// la medianoche (D-17.3): el horario nocturno se modela con dos filas.
/// </summary>
public sealed class FranjaAtencion
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public FranjaAtencion()
    {
    }

    public Guid Id { get; private set; }

    public Guid LavaderoId { get; private set; }

    /// <summary>Día de la semana ISO-8601: 1 = lunes … 7 = domingo.</summary>
    public int DiaSemana { get; private set; }

    public TimeOnly Apertura { get; private set; }

    public TimeOnly Cierre { get; private set; }

    /// <summary>Baja lógica: una franja inactiva no habilita reservas.</summary>
    public bool Activo { get; private set; } = true;

    public DateTime CreadoEn { get; private set; }

    /// <summary>
    /// Crea una franja activa. Rechaza días fuera de 1..7 y rangos que no
    /// avanzan en el tiempo (D-17.3).
    /// </summary>
    public static FranjaAtencion Crear(
        Guid lavaderoId,
        int diaSemana,
        TimeOnly apertura,
        TimeOnly cierre,
        DateTime creadoEnUtc)
    {
        if (lavaderoId == Guid.Empty)
        {
            throw new ArgumentException(
                "La franja pertenece a un lavadero.", nameof(lavaderoId));
        }

        if (diaSemana is < 1 or > 7)
        {
            throw new ArgumentException(
                $"El día {diaSemana} no es válido: se espera 1 (lunes) a 7 (domingo).",
                nameof(diaSemana));
        }

        if (apertura >= cierre)
        {
            throw new ArgumentException(
                "La apertura debe ser anterior al cierre.", nameof(cierre));
        }

        return new FranjaAtencion
        {
            Id = Guid.NewGuid(),
            LavaderoId = lavaderoId,
            DiaSemana = diaSemana,
            Apertura = apertura,
            Cierre = cierre,
            Activo = true,
            CreadoEn = creadoEnUtc,
        };
    }

    /// <summary>Baja lógica, idempotente.</summary>
    public void Desactivar() => Activo = false;

    /// <summary>Reactivación, idempotente.</summary>
    public void Activar() => Activo = true;
}
