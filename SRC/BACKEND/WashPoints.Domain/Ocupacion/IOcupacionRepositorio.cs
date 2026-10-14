using WashPoints.Domain.Turnos;

namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Persistencia de turnos. Puerto del módulo Ocupacion (ARCHITECTURE §2.2).
///
/// Todo lo que escribe en la tabla `turnos` pasa por este puerto: es la
/// garantía de RB-2. Si otro módulo necesita crear una ocupación, lo hace a
/// través de <see cref="IOcupacionServicio"/>, nunca insertando directo.
/// </summary>
public interface IOcupacionRepositorio
{
    Task AgregarAsync(Turno turno, CancellationToken ct);

    Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct);

    /// <summary>
    /// Busca el turno por su clave de idempotencia. Es la reconsulta que hace
    /// la reserva cuando el INSERT choca por 23P01 o 23505 (SPECS/slice-01 T-14).
    /// </summary>
    Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// Lista los turnos de un lavadero cuyo inicio cae en <c>[desdeUtc, hastaUtc)</c>.
    /// Alimenta la caché offline de comprobantes del día (US-A03 CA-3.1).
    /// </summary>
    Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct);

    /// <summary>
    /// ¿Hay algún turno vigente que bloquee el puesto a partir de <paramref name="ahoraUtc"/>?
    /// Vigente = los estados del EXCLUDE que ocupan (<c>confirmado</c>/<c>en_curso</c>)
    /// más el <c>pendiente_pago</c> cuya seña no venció (<c>expira_en &gt; ahoraUtc</c>),
    /// siempre que el turno todavía no haya terminado (<c>upper(rango) &gt; ahoraLocal</c>).
    /// Es el insumo de D5: no se desactiva un puesto con turnos activos (SPECS/slice-04).
    /// </summary>
    Task<bool> TieneTurnosVigentesAsync(
        Guid lavaderoId,
        Guid puestoId,
        DateTime ahoraUtc,
        CancellationToken ct);

    /// <summary>
    /// Turnos <c>confirmado</c> cuyo inicio ya cayó dentro de la ventana vencida
    /// (<c>lower(rango) &lt;= limiteLocal</c>). El llamador pasa
    /// <c>limiteUtc = ahora - ventana</c>. Insumo del worker de no_presentado.
    /// </summary>
    Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(
        DateTime limiteUtc,
        CancellationToken ct);

    Task GuardarAsync(CancellationToken ct);
}
