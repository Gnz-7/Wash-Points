namespace WashPoints.Domain.Turnos;

/// <summary>
/// Intervalo de ocupación de un turno, en UTC y con semántica semiabierta
/// <c>[InicioUtc, FinUtc)</c>: un turno que termina 11:30 y otro que arranca
/// 11:30 no se solapan (ARCHITECTURE §4.2).
///
/// Vive en el dominio como un único valor porque en la base de datos es una
/// única columna <c>tsrange</c>. Tener dos atributos sueltos en la entidad y
/// dos columnas sueltas habría roto el EXCLUDE, que solapa un rango, no dos
/// timestamps.
///
/// <para>
/// Los extremos se guardan en UTC. La traducción al <c>tsrange</c> sin zona
/// con hora local es responsabilidad de la capa de infraestructura
/// (ARCHITECTURE §10.6) — este tipo no sabe nada de husos.
/// </para>
/// </summary>
public readonly record struct RangoTurno
{
    public RangoTurno(DateTime inicioUtc, DateTime finUtc)
    {
        if (inicioUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "El inicio del rango se expresa en UTC (ARCHITECTURE §10.6).", nameof(inicioUtc));
        }

        if (finUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "El fin del rango se expresa en UTC (ARCHITECTURE §10.6).", nameof(finUtc));
        }

        if (finUtc <= inicioUtc)
        {
            throw new ArgumentException(
                "El fin del rango debe ser posterior a su inicio.", nameof(finUtc));
        }

        InicioUtc = inicioUtc;
        FinUtc = finUtc;
    }

    /// <summary>Extremo inclusivo del intervalo, en UTC.</summary>
    public DateTime InicioUtc { get; }

    /// <summary>Extremo exclusivo del intervalo, en UTC.</summary>
    public DateTime FinUtc { get; }

    /// <summary>Duración del intervalo.</summary>
    public TimeSpan Duracion => FinUtc - InicioUtc;

    /// <summary>True si ambos intervalos comparten al menos un instante.</summary>
    public bool SeSolapaCon(RangoTurno otro) =>
        InicioUtc < otro.FinUtc && otro.InicioUtc < FinUtc;
}
