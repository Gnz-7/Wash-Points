using MercadoPago.Client;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;
using MercadoPago.Error;
using MercadoPago.Http;
using MercadoPago.Resource.Payment;
using WashPoints.Domain.Puertos;

namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Adaptador real de Mercado Pago (slice-15, D-15.1/D-15.2; token por
/// lavadero del slice-20, D-20.4).
///
/// Toda la superficie del SDK vive aquí: el resto del sistema sólo conoce
/// <see cref="IPasarelaPagos"/>. El <see cref="IHttpClient"/> se inyecta para
/// que los tests usen un handler falso y nunca salgan a la red.
///
/// Reglas que este adaptador no puede romper:
/// - un pago sólo se considera aprobado si la pasarela lo dice (RB-1);
/// - un reintegro nunca se inventa: si la pasarela no confirma, se informa
///   <c>Pendiente</c> o <c>Rechazado</c> (RB-1/D-15.4).
/// </summary>
public sealed class PasarelaPagosMercadoPago : IPasarelaPagos
{
    private const string Moneda = "ARS";

    private readonly OpcionesMercadoPago _opciones;
    private readonly IHttpClient _http;

    public PasarelaPagosMercadoPago(OpcionesMercadoPago opciones, IHttpClient? http = null)
    {
        _opciones = opciones;
        _http = http ?? new DefaultHttpClient();
    }

    public async Task<PreferenciaCreada> CrearPreferenciaAsync(
        PreferenciaPago preferencia,
        string accessToken,
        CancellationToken ct)
    {
        // La reserva arma una URL por turno (`/turnos/{id}/comprobante?origen=mp`)
        // para que la web reconcilie al volver. Sin ella se usa `Pagos:UrlWeb`
        // y, como último recurso, la URL configurada. El retorno del navegador
        // nunca confirma nada (RB-1).
        var retorno = preferencia.UrlRetorno;
        if (string.IsNullOrWhiteSpace(retorno))
        {
            retorno = string.IsNullOrWhiteSpace(_opciones.UrlWeb)
                ? _opciones.UrlRetorno
                : $"{_opciones.UrlWeb!.TrimEnd('/')}/turnos/{preferencia.TurnoId}/comprobante?origen=mp";
        }

        var request = new PreferenceRequest
        {
            Items =
            [
                new PreferenceItemRequest
                {
                    Id = preferencia.TurnoId.ToString(),
                    Title = preferencia.Descripcion,
                    Quantity = 1,
                    UnitPrice = preferencia.Monto,
                    CurrencyId = Moneda,
                },
            ],
            ExternalReference = preferencia.ReferenciaExterna,
            NotificationUrl = _opciones.UrlNotificacion,
            BackUrls = new PreferenceBackUrlsRequest
            {
                Success = retorno,
                Failure = retorno,
                Pending = retorno,
            },
            AutoReturn = "approved",
            StatementDescriptor = "WASHPOINTS",
        };

        try
        {
            var creada = await new PreferenceClient(_http).CreateAsync(
                request,
                Opciones(accessToken, $"turno-{preferencia.TurnoId}"),
                ct);

            var url = creada.InitPoint ?? creada.SandboxInitPoint;
            if (string.IsNullOrWhiteSpace(creada.Id) || string.IsNullOrWhiteSpace(url))
            {
                throw new PasarelaNoDisponibleException(
                    "Mercado Pago no devolvió una preferencia utilizable.");
            }

            return new PreferenciaCreada(creada.Id, url);
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 401)
        {
            throw new PasarelaTokenInvalidoException(
                "Mercado Pago rechazó el token del lavadero al crear la preferencia.");
        }
        catch (MercadoPagoApiException ex)
        {
            throw new PasarelaNoDisponibleException(
                $"Mercado Pago rechazó la preferencia (HTTP {ex.StatusCode?.ToString() ?? "sin respuesta"}).",
                ex);
        }
        catch (MercadoPagoException ex)
        {
            throw new PasarelaNoDisponibleException(
                "No se pudo contactar a Mercado Pago para crear la preferencia.",
                ex);
        }
    }

    public async Task<ResultadoVerificacionPago> VerificarPagoAsync(
        string pagoIdExterno,
        string accessToken,
        CancellationToken ct)
    {
        if (!long.TryParse(pagoIdExterno, out var pagoId))
        {
            // El respaldo de versiones anteriores pasaba el id de preferencia.
            // No es un pago: se informa pendiente en vez de inventar un estado.
            return ResultadoVerificacionPago.Pendiente();
        }

        try
        {
            var pago = await new PaymentClient(_http).GetAsync(pagoId, Opciones(accessToken), ct);
            return MapearPago(pago);
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 404)
        {
            return ResultadoVerificacionPago.Pendiente();
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 401)
        {
            throw new PasarelaTokenInvalidoException(
                "Mercado Pago rechazó el token del lavadero al verificar el pago.");
        }
        catch (MercadoPagoApiException ex)
        {
            throw new PasarelaNoDisponibleException(
                $"Mercado Pago no devolvió el pago (HTTP {ex.StatusCode?.ToString() ?? "sin respuesta"}).",
                ex);
        }
        catch (MercadoPagoException ex)
        {
            throw new PasarelaNoDisponibleException(
                "No se pudo contactar a Mercado Pago para verificar el pago.",
                ex);
        }
    }

    public async Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(
        Guid turnoId,
        string accessToken,
        CancellationToken ct)
    {
        var request = new SearchRequest
        {
            Limit = 10,
            Filters = new Dictionary<string, object>
            {
                ["external_reference"] = turnoId.ToString(),
                ["sort"] = "date_created",
                ["criteria"] = "desc",
            },
        };

        try
        {
            var pagina = await new PaymentClient(_http).SearchAsync(request, Opciones(accessToken), ct);
            var pago = pagina.Results?.FirstOrDefault();
            return pago is null
                ? ResultadoVerificacionPago.Pendiente(turnoId)
                : MapearPago(pago);
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 401)
        {
            throw new PasarelaTokenInvalidoException(
                "Mercado Pago rechazó el token del lavadero al buscar el pago.");
        }
        catch (MercadoPagoApiException ex)
        {
            throw new PasarelaNoDisponibleException(
                $"Mercado Pago no devolvió la búsqueda del pago (HTTP {ex.StatusCode?.ToString() ?? "sin respuesta"}).",
                ex);
        }
        catch (MercadoPagoException ex)
        {
            throw new PasarelaNoDisponibleException(
                "No se pudo contactar a Mercado Pago para buscar el pago del turno.",
                ex);
        }
    }

    public async Task<ResultadoReembolso> ReembolsarAsync(
        string pagoId,
        decimal monto,
        string? claveIdempotencia,
        string accessToken,
        CancellationToken ct)
    {
        if (!long.TryParse(pagoId, out var id))
        {
            return ResultadoReembolso.Rechazado(
                "El identificador de pago no es válido.", reintentable: false);
        }

        if (monto <= 0)
        {
            return ResultadoReembolso.Rechazado(
                "El monto a reintegrar debe ser positivo.", reintentable: false);
        }

        try
        {
            var pago = await new PaymentClient(_http).GetAsync(id, Opciones(accessToken), ct);
            var total = pago.TransactionAmount ?? 0m;
            if (total <= 0)
            {
                return ResultadoReembolso.Rechazado(
                    "Mercado Pago no informó el monto del pago.", reintentable: false);
            }

            if (monto > total)
            {
                return ResultadoReembolso.Rechazado(
                    "El monto a reintegrar supera el pago original.", reintentable: false);
            }

            var esTotal = monto == total;
            var reembolso = await new PaymentRefundClient(_http).RefundAsync(
                id,
                esTotal ? null : monto,
                Opciones(accessToken, claveIdempotencia),
                ct);

            return MapearReembolso(reembolso);
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 401)
        {
            throw new PasarelaTokenInvalidoException(
                "Mercado Pago rechazó el token del lavadero al reintegrar.");
        }
        catch (MercadoPagoApiException ex)
        {
            var reintentable = ex.StatusCode is 408 or 429 or >= 500;
            return ResultadoReembolso.Rechazado(
                $"Mercado Pago rechazó el reintegro (HTTP {ex.StatusCode?.ToString() ?? "sin respuesta"}).",
                reintentable);
        }
        catch (MercadoPagoException)
        {
            return ResultadoReembolso.Rechazado(
                "No se pudo contactar a Mercado Pago para reintegrar la seña.",
                reintentable: true);
        }
    }

    public async Task<ResultadoReembolso> ConsultarReembolsoAsync(
        string pagoId,
        string reembolsoId,
        string accessToken,
        CancellationToken ct)
    {
        if (!long.TryParse(pagoId, out var id) || !long.TryParse(reembolsoId, out var refundId))
        {
            return ResultadoReembolso.Rechazado(
                "El identificador de pago o de reintegro no es válido.", reintentable: false);
        }

        try
        {
            var reembolso = await new PaymentRefundClient(_http).GetAsync(id, refundId, Opciones(accessToken), ct);
            return MapearReembolso(reembolso);
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 404)
        {
            return ResultadoReembolso.Rechazado(
                "Mercado Pago no encontró el reintegro consultado.", reintentable: false);
        }
        catch (MercadoPagoApiException ex) when (ex.StatusCode == 401)
        {
            throw new PasarelaTokenInvalidoException(
                "Mercado Pago rechazó el token del lavadero al consultar el reintegro.");
        }
        catch (MercadoPagoApiException ex)
        {
            var reintentable = ex.StatusCode is 408 or 429 or >= 500;
            return ResultadoReembolso.Rechazado(
                $"Mercado Pago no devolvió el reintegro (HTTP {ex.StatusCode?.ToString() ?? "sin respuesta"}).",
                reintentable);
        }
        catch (MercadoPagoException)
        {
            return ResultadoReembolso.Rechazado(
                "No se pudo contactar a Mercado Pago para consultar el reintegro.",
                reintentable: true);
        }
    }

    private static RequestOptions Opciones(string accessToken, string? claveIdempotencia = null)
    {
        var opciones = new RequestOptions { AccessToken = accessToken };
        if (!string.IsNullOrWhiteSpace(claveIdempotencia))
        {
            opciones.CustomHeaders["X-Idempotency-Key"] = claveIdempotencia;
        }

        return opciones;
    }

    private static ResultadoVerificacionPago MapearPago(Payment pago)
    {
        var turnoId = Guid.TryParse(pago.ExternalReference, out var id) ? id : (Guid?)null;
        var pagoId = pago.Id?.ToString();

        return pago.Status switch
        {
            "approved" when turnoId is not null && pagoId is not null =>
                ResultadoVerificacionPago.Aprobado(turnoId.Value, pagoId),
            "rejected" or "cancelled" or "charged_back" =>
                ResultadoVerificacionPago.Rechazado(turnoId, pagoId),
            _ => ResultadoVerificacionPago.Pendiente(turnoId, pagoId),
        };
    }

    private static ResultadoReembolso MapearReembolso(PaymentRefund reembolso)
    {
        var reembolsoId = reembolso.Id?.ToString();

        return reembolso.Status switch
        {
            "approved" when reembolsoId is not null => ResultadoReembolso.Aprobado(reembolsoId),
            "pending" or "in_process" => ResultadoReembolso.Pendiente(reembolsoId),
            _ => ResultadoReembolso.Rechazado(
                $"Mercado Pago devolvió el reintegro en estado {reembolso.Status ?? "desconocido"}.",
                reintentable: false),
        };
    }
}
