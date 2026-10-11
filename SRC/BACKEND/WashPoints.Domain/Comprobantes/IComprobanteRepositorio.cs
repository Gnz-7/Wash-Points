namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// Persistencia de comprobantes (ARCHITECTURE §7.1; SPECS/slice-02 §3).
///
/// Los comprobantes son el token de presencia de RB-3. La lectura por hash
/// alimenta el canje; la lectura por turno permite re-servir el comprobante al
/// cliente (US-C08 CA-8.4). El token en claro nunca se persiste.
/// </summary>
public interface IComprobanteRepositorio
{
    /// <summary>Obtiene el comprobante de un turno (a lo sumo uno: UNIQUE(turno_id)).</summary>
    Task<Comprobante?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct);

    /// <summary>Obtiene el comprobante por el hash del token (base del canje).</summary>
    Task<Comprobante?> ObtenerPorHashAsync(string hash, CancellationToken ct);

    /// <summary>
    /// Marca el canje de forma atómica: un único <c>UPDATE ... WHERE usado_at IS NULL</c>
    /// que devuelve las filas afectadas. Bajo concurrencia, exactamente un
    /// llamador obtiene <c>1</c>; el resto, <c>0</c> (CA-S2.6). Debe ejecutarse
    /// dentro de la transacción del canje (RB-3).
    /// </summary>
    Task<int> MarcarCanjeadoSiDisponibleAsync(
        Guid comprobanteId,
        DateTime usadoEnUtc,
        bool offline,
        CancellationToken ct);

    /// <summary>
    /// Devuelve los hash de los comprobantes vigentes (no usados y no vencidos)
    /// de los turnos indicados. Nunca expone el token ni el ciphertext; alimenta
    /// la caché offline del administrador (US-A03 CA-3.1).
    /// </summary>
    Task<IReadOnlyList<string>> ListarHashesVigentesAsync(
        IReadOnlyCollection<Guid> turnoIds,
        DateTime ahoraUtc,
        CancellationToken ct);
}
