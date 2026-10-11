namespace WashPoints.Domain.Comprobantes;

/// <summary>
/// Comprobante digital: el token de presencia que valida RB-3 al arribo del
/// cliente (ARCHITECTURE §4.1, §5).
///
/// <para>
/// El token en claro <b>nunca</b> se persiste: se guarda su <see cref="Hash"/>
/// SHA-256 (búsqueda, unicidad y canje) y una copia cifrada simétrica
/// (<see cref="TokenCifrado"/>, AES-GCM) que permite volver a mostrárselo al
/// cliente sin exponerlo en reposo (SPECS/slice-02 §3, decisión D1).
/// </para>
/// </summary>
public sealed class Comprobante
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Comprobante()
    {
    }

    private Comprobante(
        Guid id,
        Guid turnoId,
        string hash,
        string tokenCifrado,
        DateTime expiraEn,
        DateTime creadoEn)
    {
        Id = id;
        TurnoId = turnoId;
        Hash = hash;
        TokenCifrado = tokenCifrado;
        ExpiraEn = expiraEn;
        CreadoEn = creadoEn;
    }

    public Guid Id { get; private set; }

    /// <summary>Turno al que pertenece. Un turno tiene exactamente un comprobante.</summary>
    public Guid TurnoId { get; private set; }

    /// <summary>Hash SHA-256 del token (hex). Localiza el comprobante en el canje.</summary>
    public string Hash { get; private set; } = string.Empty;

    /// <summary>Token cifrado simétricamente (AES-GCM). Nunca el claro en reposo.</summary>
    public string TokenCifrado { get; private set; } = string.Empty;

    /// <summary>Fin de vigencia del comprobante (UTC).</summary>
    public DateTime ExpiraEn { get; private set; }

    /// <summary>Instante del canje exitoso (UTC). <c>null</c> mientras no se canjeó.</summary>
    public DateTime? UsadoAt { get; private set; }

    /// <summary>El canje se registró sin red y espera reconciliación.</summary>
    public bool ValidadoOffline { get; private set; }

    public DateTime CreadoEn { get; private set; }

    /// <summary>Indica si el comprobante ya fue canjeado.</summary>
    public bool Usado => UsadoAt is not null;

    /// <summary>Indica si el comprobante ya venció respecto de <paramref name="ahoraUtc"/>.</summary>
    public bool EstaVencido(DateTime ahoraUtc) => ahoraUtc >= ExpiraEn;

    /// <summary>
    /// Verifica que el comprobante pueda canjearse (RB-3): no usado y no
    /// vencido. Lanza la excepción de dominio correspondiente en caso contrario.
    ///
    /// "Ya usado" tiene prioridad sobre "vencido": define el código de error del
    /// canje concurrente (US-A04 CA-4.2, CA-S2.6).
    /// </summary>
    public void AsegurarCanjeable(DateTime ahoraUtc)
    {
        if (Usado)
        {
            throw new ComprobanteYaUtilizadoException(TurnoId);
        }

        if (EstaVencido(ahoraUtc))
        {
            throw new ComprobanteVencidoException(TurnoId, ExpiraEn);
        }
    }

    /// <summary>
    /// Registra el canje. El llamador ya garantizó la unicidad (validación más
    /// el UPDATE condicional de la misma transacción); esta mutación refleja el
    /// resultado. <paramref name="offline"/> marca un canje reconciliado sin red.
    /// </summary>
    public void RegistrarCanje(DateTime cuandoUtc, bool offline = false)
    {
        if (cuandoUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("El instante de canje debe estar en UTC.", nameof(cuandoUtc));
        }

        UsadoAt = cuandoUtc;
        ValidadoOffline = offline;
    }

    /// <summary>
    /// Emite el comprobante de un turno confirmado (SPECS/slice-02 T-26). El
    /// llamador ya generó el <paramref name="hash"/> y cifró el token; esta
    /// factoría solo arma el agregado.
    /// </summary>
    public static Comprobante Emitir(
        Guid turnoId,
        string hash,
        string tokenCifrado,
        DateTime expiraEnUtc,
        DateTime? creadoEnUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenCifrado);

        if (expiraEnUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "El vencimiento del comprobante debe estar en UTC.", nameof(expiraEnUtc));
        }

        var creadoEn = creadoEnUtc ?? DateTime.UtcNow;
        if (creadoEn.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "La fecha de emisión debe estar en UTC.", nameof(creadoEnUtc));
        }

        return new Comprobante(
            Guid.NewGuid(), turnoId, hash.Trim(), tokenCifrado, expiraEnUtc, creadoEn);
    }
}
