using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Application.Comprobantes;

/// <summary>
/// Caché de vigentes para el modo offline del administrador (SPECS/slice-02 T-30;
/// US-A03 CA-3.1).
///
/// Devuelve los <b>hash</b> de los comprobantes vigentes de los turnos del día
/// del lavadero. Nunca expone el token ni el ciphertext: la app solo compara el
/// hash del comprobante escaneado contra esta lista.
/// </summary>
public sealed class VigentesComprobanteServicio
{
    private readonly IOcupacionRepositorio _turnos;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly TimeProvider _reloj;

    public VigentesComprobanteServicio(
        IOcupacionRepositorio turnos,
        IComprobanteRepositorio comprobantes,
        TimeProvider reloj)
    {
        _turnos = turnos;
        _comprobantes = comprobantes;
        _reloj = reloj;
    }

    /// <summary>
    /// Lista los hash vigentes del lavadero con inicio en <c>[desdeUtc, hastaUtc)</c>.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListarAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct)
    {
        var turnos = await _turnos.ListarDelDiaAsync(lavaderoId, desdeUtc, hastaUtc, ct);
        if (turnos.Count == 0)
        {
            return Array.Empty<string>();
        }

        var ids = turnos.Select(t => t.Id).ToList();
        return await _comprobantes.ListarHashesVigentesAsync(
            ids, _reloj.GetUtcNow().UtcDateTime, ct);
    }
}
