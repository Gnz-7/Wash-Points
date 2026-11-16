using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.MercadoPago;
using WashPoints.Infrastructure.Persistencia;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Vinculación OAuth de Mercado Pago por lavadero (slice-20, D-20.3).
///
/// - <see cref="ConstruirUrlAutorizacionAsync"/> arma la URL de autorización
///   de MP con un <c>state</c> firmado (anti-CSRF, vence en 10 minutos).
/// - <see cref="VincularConCodeAsync"/> valida el <c>state</c>, intercambia el
///   <c>code</c> por tokens, los cifra (AES-GCM con
///   <c>MercadoPago:ClaveTokens</c>) y los persiste en la fila del lavadero
///   marcando <c>mp_vinculado</c>. Re-vincular reemplaza el par.
///
/// Sin credenciales de app o sin clave de cifrado, ambos pasos fallan de forma
/// explícita con <see cref="MercadoPagoNoConfiguradoException"/> (el endpoint
/// responde 503); nada degradado ni silencioso.
///
/// Además implementa <see cref="IObtenedorTokensMp"/> (D-20.8): la puerta de
/// los tokens que la reserva, el webhook y el worker de reembolsos usan para
/// operar con la cuenta del lavadero. El token se entrega descifrado dentro
/// del proceso y un refresh fallido desvincula al lavadero (motivo guardado
/// para el caller que quiera registrarlo).
/// </summary>
public sealed class VinculacionMpServicio : IObtenedorTokensMp
{
    private const string UrlAutorizacion = "https://auth.mercadopago.com/authorization";

    private readonly WashPointsDbContext _contexto;
    private readonly OpcionesVinculacionMp _opciones;
    private readonly ICifradorTokensMp _cifrador;
    private readonly HttpClient _http;
    private readonly TimeProvider _reloj;

    public VinculacionMpServicio(
        WashPointsDbContext contexto,
        OpcionesVinculacionMp opciones,
        ICifradorTokensMp cifrador,
        HttpClient http,
        TimeProvider reloj)
    {
        _contexto = contexto;
        _opciones = opciones;
        _cifrador = cifrador;
        _http = http;
        _reloj = reloj;
    }

    /// <summary>
    /// URL de autorización de MP a la que el panel redirige el navegador.
    /// Lanza <see cref="LavaderoNoEncontradoException"/> si el lavadero no
    /// existe y <see cref="MercadoPagoNoConfiguradoException"/> sin
    /// credenciales de app o clave de cifrado.
    /// </summary>
    public async Task<string> ConstruirUrlAutorizacionAsync(Guid lavaderoId, CancellationToken ct)
    {
        AsegurarConfigurado();

        _ = await ObtenerLavaderoAsync(lavaderoId, ct);

        var firmador = new FirmadorEstadoOauth(_opciones.ClaveTokens, _reloj);
        var (state, _) = firmador.Firmar(lavaderoId);

        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = _opciones.ClientId;
        query["response_type"] = "code";
        query["redirect_uri"] = _opciones.RedirectUri;
        query["state"] = state;

        return $"{UrlAutorizacion}?{query}";
    }

    /// <summary>
    /// Completa la vinculación: valida el <c>state</c>, intercambia el
    /// <c>code</c>, cifra los tokens y los persiste. Devuelve el lavadero
    /// vinculado. Lanza <see cref="EstadoOauthInvalidoException"/> con un
    /// <c>state</c> inválido/vencido, <see cref="IntercambioOauthFallidoException"/>
    /// si MP rechaza el código y <see cref="MercadoPagoNoConfiguradoException"/>
    /// sin configuración.
    /// </summary>
    public async Task<Guid> VincularConCodeAsync(string state, string code, CancellationToken ct)
    {
        AsegurarConfigurado();

        var firmador = new FirmadorEstadoOauth(_opciones.ClaveTokens, _reloj);
        var lavaderoId = firmador.Validar(state)
            ?? throw new EstadoOauthInvalidoException();

        var lavadero = await ObtenerLavaderoAsync(lavaderoId, ct);

        var cliente = new ClienteOauthMp(_http);
        var tokens = await cliente.IntercambiarCodigoAsync(code, _opciones, ct);

        lavadero.VincularMercadoPago(
            _cifrador.Cifrar(tokens.AccessToken),
            _cifrador.Cifrar(tokens.RefreshToken),
            tokens.UserId,
            _reloj.GetUtcNow());
        await _contexto.SaveChangesAsync(ct);

        return lavaderoId;
    }

    /// <summary>
    /// Entrega el token de acceso del lavadero descifrado, listo para operar
    /// contra MP con su cuenta (D-20.8). Lanza
    /// <see cref="MercadoPagoNoVinculadoException"/> si el lavadero no tiene
    /// una cuenta vinculada (o la perdió por un refresh fallido).
    /// </summary>
    public async Task<string> ObtenerTokenVigenteAsync(Guid lavaderoId, CancellationToken ct)
    {
        var lavadero = await _contexto.Lavaderos
            .FirstOrDefaultAsync(l => l.Id == lavaderoId, ct)
            ?? throw new MercadoPagoNoVinculadoException(
                $"El lavadero {lavaderoId} no existe, así que no tiene una cuenta de Mercado Pago vinculada.");

        if (!lavadero.MpVinculado || string.IsNullOrWhiteSpace(lavadero.MpAccessToken))
        {
            throw new MercadoPagoNoVinculadoException(
                $"El lavadero {lavadero.Nombre} no vinculó su cuenta de Mercado Pago.");
        }

        return _cifrador.Descifrar(lavadero.MpAccessToken);
    }

    /// <summary>
    /// Renueva el par de tokens del lavadero contra el OAuth de MP con su
    /// <c>refresh_token</c> y persiste el par nuevo cifrado (reemplaza, D-20.8).
    /// Si MP rechaza el refresh (token revocado/expirado), desvincula al
    /// lavadero —la reserva vuelve a responder 422 y el panel ofrece
    /// re-vincular— y propaga <see cref="MercadoPagoNoVinculadoException"/>.
    /// </summary>
    public async Task<string> RefrescarTokenAsync(Guid lavaderoId, CancellationToken ct)
    {
        var lavadero = await _contexto.Lavaderos
            .FirstOrDefaultAsync(l => l.Id == lavaderoId, ct)
            ?? throw new MercadoPagoNoVinculadoException(
                $"El lavadero {lavaderoId} no existe, así que no tiene una cuenta de Mercado Pago vinculada.");

        if (!lavadero.MpVinculado || string.IsNullOrWhiteSpace(lavadero.MpRefreshToken))
        {
            throw new MercadoPagoNoVinculadoException(
                $"El lavadero {lavadero.Nombre} no vinculó su cuenta de Mercado Pago.");
        }

        var refreshToken = _cifrador.Descifrar(lavadero.MpRefreshToken);
        var cliente = new ClienteOauthMp(_http);
        TokensMp tokens;

        try
        {
            tokens = await cliente.IntercambiarRefreshTokenAsync(refreshToken, _opciones, ct);
        }
        catch (IntercambioOauthFallidoException excepcion)
        {
            lavadero.DesvincularMercadoPago(
                $"El refresh del token de Mercado Pago falló: {excepcion.Message}");
            await _contexto.SaveChangesAsync(ct);

            throw new MercadoPagoNoVinculadoException(
                $"El refresh del token de Mercado Pago falló y la cuenta del lavadero {lavadero.Nombre} quedó desvinculada. Re-vincular repara el flujo.");
        }

        lavadero.VincularMercadoPago(
            _cifrador.Cifrar(tokens.AccessToken),
            _cifrador.Cifrar(tokens.RefreshToken),
            tokens.UserId,
            _reloj.GetUtcNow());
        await _contexto.SaveChangesAsync(ct);

        return tokens.AccessToken;
    }

    private async Task<Domain.Catalogo.Lavadero> ObtenerLavaderoAsync(Guid lavaderoId, CancellationToken ct) =>
        await _contexto.Lavaderos.FirstOrDefaultAsync(l => l.Id == lavaderoId, ct)
        ?? throw new LavaderoDeVinculacionInexistenteException(lavaderoId);

    private void AsegurarConfigurado()
    {
        if (_opciones.CredencialesFaltantes().Count > 0 || !_cifrador.EstaConfigurado)
        {
            throw new MercadoPagoNoConfiguradoException();
        }
    }
}
