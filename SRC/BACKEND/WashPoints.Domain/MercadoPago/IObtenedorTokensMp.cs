namespace WashPoints.Domain.MercadoPago;

/// <summary>
/// Puerto de obtención del token vigente de Mercado Pago de un lavadero.
/// La implementación descifra el token en reposo y lo refresca cuando
/// expiró; si el refresh falla, desvincula al lavadero y avisa con
/// <see cref="MercadoPagoNoVinculadoException"/> (SPECS/slice-20 D-20.8).
/// </summary>
public interface IObtenedorTokensMp
{
    /// <summary>
    /// Devuelve el token de acceso vigente del lavadero, descifrado. El
    /// refresco por vencimiento lo resuelve quien llama ante un
    /// <c>401</c>, con <see cref="RefrescarTokenAsync"/>.
    /// </summary>
    Task<string> ObtenerTokenVigenteAsync(Guid lavaderoId, CancellationToken ct);

    /// <summary>
    /// Refresca el token con el <c>mp_refresh_token</c> del lavadero y
    /// persiste el nuevo par cifrado. Un refresh fallido desvincula al
    /// lavadero (con motivo) y lanza
    /// <see cref="MercadoPagoNoVinculadoException"/>.
    /// </summary>
    Task<string> RefrescarTokenAsync(Guid lavaderoId, CancellationToken ct);
}
