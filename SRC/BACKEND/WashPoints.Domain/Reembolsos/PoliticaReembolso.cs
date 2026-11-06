namespace WashPoints.Domain.Reembolsos;

/// <summary>
/// Regla de reintegro de la seña al cancelar un turno (SPECS/slice-14 D-14.6).
///
/// Se evalúa contra el inicio del turno, no contra su creación: lo que se
/// protege es el tiempo del lavadero. El cálculo es puro —no toca relojes ni
/// base de datos— porque la hora llega como argumento y así se puede testear
/// el borde exacto del plazo.
/// </summary>
public sealed class PoliticaReembolso
{
    private readonly TimeSpan _anticipacionMinima;
    private readonly decimal _porcentaje;

    /// <summary>Crea la política a partir de las opciones configuradas.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Las opciones no cumplen el rango.</exception>
    public PoliticaReembolso(OpcionesReembolso opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        opciones.Validar();

        _anticipacionMinima = TimeSpan.FromMinutes(opciones.AnticipacionMinimaMinutos);
        _porcentaje = opciones.PorcentajeReintegrable;
    }

    /// <summary>Anticipación mínima exigida, en minutos.</summary>
    public int AnticipacionMinimaMinutos => (int)_anticipacionMinima.TotalMinutes;

    /// <summary>Porcentaje de la seña que se reintegra en plazo.</summary>
    public decimal PorcentajeReintegrable => _porcentaje;

    /// <summary>
    /// Decide el reintegro de un turno cancelado por el cliente.
    /// </summary>
    /// <param name="precioSena">Seña efectivamente cobrada al reservar.</param>
    /// <param name="inicioUtc">Inicio del turno, en UTC.</param>
    /// <param name="ahoraUtc">Momento de la cancelación, en UTC.</param>
    public EvaluacionReembolso Evaluar(
        decimal precioSena,
        DateTime inicioUtc,
        DateTime ahoraUtc)
    {
        // Sin seña cobrada no hay nada que devolver: el turno seguía
        // pendiente_pago y el cliente no puso dinero.
        if (precioSena <= 0m)
        {
            return new EvaluacionReembolso(
                EstadoReembolso.NoAplica,
                MotivoReembolso.SinPago,
                0m);
        }

        // Comparación inclusiva: cancelar exactamente en el límite reintegra.
        if (ahoraUtc + _anticipacionMinima > inicioUtc)
        {
            return new EvaluacionReembolso(
                EstadoReembolso.NoAplica,
                MotivoReembolso.ClienteFueraDePlazo,
                0m);
        }

        var monto = Math.Round(
            precioSena * _porcentaje / 100m,
            2,
            MidpointRounding.AwayFromZero);

        // Un porcentaje cero está en plazo, pero no devuelve nada: no tiene
        // sentido crear una fila pendiente de pago para un importe nulo.
        if (monto <= 0m)
        {
            return new EvaluacionReembolso(
                EstadoReembolso.NoAplica,
                MotivoReembolso.ClienteEnPlazo,
                0m);
        }

        return new EvaluacionReembolso(
            EstadoReembolso.Pendiente,
            MotivoReembolso.ClienteEnPlazo,
            monto);
    }
}
