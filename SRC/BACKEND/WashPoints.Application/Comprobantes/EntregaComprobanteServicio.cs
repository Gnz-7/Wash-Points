using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Application.Comprobantes;

/// <summary>Comprobante listo para mostrar al cliente: QR, código de respaldo y vencimiento.</summary>
public sealed record ComprobanteEntregable(string PayloadQr, string CodigoRespaldo, DateTime ExpiraEn);

/// <summary>
/// Entrega el comprobante al cliente (SPECS/slice-02 T-27, US-C08 CA-8.4).
///
/// Descifra el token desde la copia cifrada y reconstruye las dos
/// representaciones (QR y código Base32), ambas apuntando al mismo token
/// canónico (CA-S2.3). Es la única superficie que expone el token en claro.
/// </summary>
public sealed class EntregaComprobanteServicio
{
    private readonly IOcupacionRepositorio _turnos;
    private readonly IComprobanteRepositorio _comprobantes;
    private readonly ICifradorComprobante _cifrador;
    private readonly IGeneradorComprobante _generador;

    public EntregaComprobanteServicio(
        IOcupacionRepositorio turnos,
        IComprobanteRepositorio comprobantes,
        ICifradorComprobante cifrador,
        IGeneradorComprobante generador)
    {
        _turnos = turnos;
        _comprobantes = comprobantes;
        _cifrador = cifrador;
        _generador = generador;
    }

    /// <summary>
    /// Devuelve el comprobante del turno, o <c>null</c> si el turno no existe o
    /// todavía no fue confirmado (no tiene comprobante).
    /// </summary>
    public async Task<ComprobanteEntregable?> ObtenerAsync(Guid turnoId, CancellationToken ct)
    {
        var turno = await _turnos.ObtenerAsync(turnoId, ct);
        if (turno is null)
        {
            return null;
        }

        var comprobante = await _comprobantes.ObtenerPorTurnoAsync(turnoId, ct);
        if (comprobante is null)
        {
            return null;
        }

        var token = _cifrador.Descifrar(comprobante.TokenCifrado);
        return new ComprobanteEntregable(
            _generador.ConstruirPayloadQr(token),
            _generador.ConstruirCodigoRespaldo(token),
            comprobante.ExpiraEn);
    }
}
