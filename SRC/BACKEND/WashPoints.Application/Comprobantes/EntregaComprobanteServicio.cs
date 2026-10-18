using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Seguridad;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Comprobantes;

/// <summary>Comprobante listo para mostrar al cliente: QR, código de respaldo y vencimiento.</summary>
public sealed record ComprobanteEntregable(string PayloadQr, string CodigoRespaldo, DateTime ExpiraEn);

/// <summary>
/// Entrega el comprobante al cliente (SPECS/slice-02 T-27, US-C08 CA-8.4).
///
/// Descifra el token desde la copia cifrada y reconstruye las dos
/// representaciones (QR y código Base32), ambas apuntando al mismo token
/// canónico (CA-S2.3). Es la única superficie que expone el token en claro.
///
/// Desde slice-05b la entrega exige **propiedad**: el comprobante es un token
/// de acceso (RB-3) y el <c>turnoId</c> es un UUID enumerable, no un control de
/// acceso (D-06.3).
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
    /// Devuelve el comprobante del turno si la identidad puede verlo, o
    /// <c>null</c> en cualquier otro caso: turno inexistente, sin comprobante,
    /// sin sesión o sin propiedad. El rechazo es homogéneo a propósito —no
    /// distingue "no existe" de "no es tuyo"— para no filtrar la existencia de
    /// turnos ajenos (D-06.3, CU-10 A2, CA-9.3).
    /// </summary>
    public async Task<ComprobanteEntregable?> ObtenerAsync(
        Guid turnoId,
        IdentidadUsuario? identidad,
        CancellationToken ct)
    {
        if (identidad is null)
        {
            return null;
        }

        var turno = await _turnos.ObtenerAsync(turnoId, ct);
        if (turno is null || !PuedeVer(identidad, turno))
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

    /// <summary>
    /// Regla de propiedad (D-06.3): el cliente ve sus turnos por el vínculo de
    /// negocio; el administrador, los de su lavadero. Cualquier otra
    /// combinación queda fuera.
    /// </summary>
    private static bool PuedeVer(IdentidadUsuario identidad, Turno turno) =>
        identidad.Rol == RolUsuario.Administrador
            ? identidad.LavaderoId is { } lavaderoId && lavaderoId == turno.LavaderoId
            : identidad.ClienteId is { } clienteId && clienteId == turno.ClienteId;
}
