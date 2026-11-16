namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Lavadero (comercio). Mínimo para resolver las FK de este slice:
/// ver DOCS/ARCHITECTURE.md §10.7 — el módulo Catalogo aún no está modelado.
/// </summary>
public sealed class Lavadero
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Lavadero()
    {
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public bool Activo { get; private set; } = true;

    /// <summary>Dirección que se muestra en el buscador (CA-7.2).</summary>
    public string? Direccion { get; private set; }

    /// <summary>Latitud en grados decimales; null si no está ubicado en el mapa.</summary>
    public double? Latitud { get; private set; }

    /// <summary>Longitud en grados decimales; null si no está ubicado en el mapa.</summary>
    public double? Longitud { get; private set; }

    // --- Suscripción mensual (SPECS/slice-20, D-20.7/D-20.8) ---

    /// <summary>
    /// Indicador de suscripción mensual (mock). Nace <c>false</c> en el alta:
    /// el cobro mensual es el único que la activa (CA-S20.8).
    /// </summary>
    public bool SuscripcionActiva { get; private set; }

    /// <summary>Vencimiento de la suscripción mensual; <c>null</c> mientras está pendiente.</summary>
    public DateTimeOffset? SuscripcionVenceEl { get; private set; }

    // --- Vinculación de Mercado Pago (SPECS/slice-20, D-20.3/D-20.8) ---

    /// <summary>Estado de vinculación con Mercado Pago; requisito de la reserva (CA-S20.13).</summary>
    public bool MpVinculado { get; private set; }

    /// <summary>Access token de MP del lavadero, <b>cifrado</b> en reposo (AES-GCM).</summary>
    public string? MpAccessToken { get; private set; }

    /// <summary>Refresh token de MP del lavadero, <b>cifrado</b> en reposo (AES-GCM).</summary>
    public string? MpRefreshToken { get; private set; }

    /// <summary>Identificador de usuario de MP del lavadero, en claro (no es secreto).</summary>
    public string? MpUserId { get; private set; }

    /// <summary>Fecha de la última vinculación/re-vinculación.</summary>
    public DateTimeOffset? MpVinculadoEl { get; private set; }

    public static Lavadero Crear(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        return new Lavadero { Id = Guid.NewGuid(), Nombre = nombre.Trim() };
    }

    /// <summary>
    /// Fija la ubicación del lavadero. Las dos coordenadas van juntas: un
    /// lavadero a medio ubicar no puede aparecer en la búsqueda por
    /// proximidad (D-08.1).
    /// </summary>
    public void Ubicar(string? direccion, double latitud, double longitud)
    {
        if (double.IsNaN(latitud) || latitud is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitud), "La latitud debe estar entre -90 y 90.");
        }

        if (double.IsNaN(longitud) || longitud is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitud), "La longitud debe estar entre -180 y 180.");
        }

        Direccion = string.IsNullOrWhiteSpace(direccion) ? null : direccion.Trim();
        Latitud = latitud;
        Longitud = longitud;
    }

    /// <summary>
    /// Activa la suscripción mensual (mock) con su vencimiento. La única
    /// llamada es el cobro mensual (T-218); el alta deja la suscripción
    /// pendiente (D-20.7).
    /// </summary>
    public void ActivarSuscripcion(DateTimeOffset venceEl)
    {
        SuscripcionActiva = true;
        SuscripcionVenceEl = venceEl;
    }

    /// <summary>
    /// La suscripción (mock) está vigente si está activa y no tiene
    /// vencimiento registrado o el vencimiento es futuro (borde inclusivo).
    /// El panel la usa para el indicador y el cobro mensual para decidir el
    /// 409 (D-20.7); una suscripción pendiente no está vigente.
    /// </summary>
    public bool SuscripcionVigente(DateTimeOffset ahora) =>
        SuscripcionActiva &&
        (SuscripcionVenceEl is null || SuscripcionVenceEl >= ahora);

    /// <summary>
    /// Vincula la cuenta de Mercado Pago del lavadero con los tokens ya
    /// cifrados (AES-GCM con <c>MercadoPago:ClaveTokens</c>). Re-vincular
    /// reemplaza el par anterior, nunca acumula (CA-S20.7).
    /// </summary>
    public void VincularMercadoPago(
        string accessTokenCifrado,
        string refreshTokenCifrado,
        string mpUserId,
        DateTimeOffset vinculadoEl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessTokenCifrado);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshTokenCifrado);

        MpVinculado = true;
        MpAccessToken = accessTokenCifrado;
        MpRefreshToken = refreshTokenCifrado;
        MpUserId = string.IsNullOrWhiteSpace(mpUserId) ? null : mpUserId.Trim();
        MpVinculadoEl = vinculadoEl;
    }

    /// <summary>
    /// Desvincula la cuenta de Mercado Pago y limpia los tokens. La llama el
    /// refresco fallido (token revocado o inválido) para que la reserva
    /// responda <c>422 mercadopago_no_vinculado</c>; re-vincular repara
    /// (D-20.8).
    /// </summary>
    public void DesvincularMercadoPago(string motivo)
    {
        MpVinculado = false;
        MpAccessToken = null;
        MpRefreshToken = null;
        MpVinculadoEl = null;
    }
}
