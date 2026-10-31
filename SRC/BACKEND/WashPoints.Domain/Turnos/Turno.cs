using System.ComponentModel.DataAnnotations.Schema;

namespace WashPoints.Domain.Turnos;

/// <summary>
/// Turno de lavado. Agregado raíz de este slice.
///
/// Las fechas se guardan en UTC puro (ARCHITECTURE §10.6): la conversión a la
/// hora local del lavadero es responsabilidad de la capa de presentación, nunca
/// de esta entidad.
///
/// <para>
/// <b>Máquina de estados:</b> <see cref="TransicionarA"/> es la única vía de
/// cambiar <see cref="Estado"/> y valida contra la tabla de ARCHITECTURE §5.
/// </para>
/// </summary>
public sealed class Turno
{
    private static readonly IReadOnlyDictionary<EstadoTurno, IReadOnlySet<EstadoTurno>> Transiciones =
        new Dictionary<EstadoTurno, IReadOnlySet<EstadoTurno>>
        {
            [EstadoTurno.PendientePago] = new HashSet<EstadoTurno>
            {
                // Único camino para que un turno empiece a ocupar un puesto (RB-1):
                // solo el webhook con firma válida puede llegar hasta acá.
                EstadoTurno.Confirmado,
                EstadoTurno.Cancelado,
                // Vencimiento del TTL de la seña (SPECS/slice-01 §3). Lo ejecuta
                // el worker; libera el box del pendiente_pago.
                EstadoTurno.Expirado,
            },
            [EstadoTurno.Confirmado] = new HashSet<EstadoTurno>
            {
                EstadoTurno.EnCurso,      // canje del comprobante (RB-3)
                EstadoTurno.NoPresentado, // venció la ventana de llegada
                // Cancelación administrativa de un turno ya pagado (SPECS/slice-04,
                // D-04.3). No hay reembolso automático: eso queda abierto en §10.1.
                EstadoTurno.Cancelado,
            },
            [EstadoTurno.EnCurso] = new HashSet<EstadoTurno>
            {
                EstadoTurno.Finalizado,
            },
            // Estados terminales: sin transiciones de salida.
            [EstadoTurno.Finalizado] = new HashSet<EstadoTurno>(),
            [EstadoTurno.Cancelado] = new HashSet<EstadoTurno>(),
            [EstadoTurno.NoPresentado] = new HashSet<EstadoTurno>(),
            [EstadoTurno.Expirado] = new HashSet<EstadoTurno>(),
        };

    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Turno()
    {
    }

    private Turno(
        Guid id,
        Guid lavaderoId,
        Guid puestoId,
        Guid clienteId,
        Guid tipoLavadoId,
        RangoTurno rango,
        decimal precioSena,
        OrigenTurno origen)
    {
        Id = id;
        LavaderoId = lavaderoId;
        PuestoId = puestoId;
        ClienteId = clienteId;
        TipoLavadoId = tipoLavadoId;
        Rango = rango;
        PrecioSena = precioSena;
        Origen = origen;
        Estado = EstadoTurno.PendientePago;
        CreadoEn = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid LavaderoId { get; private set; }
    public Guid PuestoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid TipoLavadoId { get; private set; }

    /// <summary>
    /// Intervalo de ocupación. Es la propiedad que EF mapea a la columna
    /// <c>tsrange</c> de ARCHITECTURE §4.1: una sola columna, no dos timestamps.
    /// </summary>
    public RangoTurno Rango { get; private set; }

    /// <summary>Inicio del intervalo, en UTC.</summary>
    public DateTime InicioUtc => Rango.InicioUtc;

    /// <summary>Fin del intervalo, en UTC. Extremo exclusivo: dos contiguos no chocan.</summary>
    public DateTime FinUtc => Rango.FinUtc;

    public EstadoTurno Estado { get; private set; }
    public OrigenTurno Origen { get; private set; }
    public decimal PrecioSena { get; private set; }

    public string? MpPreferenceId { get; private set; }
    public DateTime CreadoEn { get; private set; }
    public DateTime? ConfirmadoEn { get; private set; }
    public DateTime? CerradoEn { get; private set; }

    /// <summary>
    /// Vence el bloqueo del <see cref="EstadoTurno.PendientePago"/> (TTL de la
    /// seña, SPECS/slice-01 §3). La liberación la ejecuta el worker al
    /// transicionar a <see cref="EstadoTurno.Expirado"/>; nunca viene del
    /// predicado del EXCLUDE. UTC, como toda hora persistida (§10.6).
    /// </summary>
    public DateTime? ExpiraEn { get; private set; }

    /// <summary>
    /// Clave de idempotencia de la reserva (SHA-256 del intento). No es un dato
    /// de negocio: su propósito es que un reintento del cliente no duplique
    /// turnos. Ver SPECS/slice-01 §3 (captura de 23P01 y 23505).
    ///
    /// <c>null</c> cuando no hay intento de reserva: el índice único parcial
    /// (<c>WHERE idempotency_key IS NOT NULL</c>) lo excluye, de modo que los
    /// turnos creados sin reserva (p. ej. demanda espontánea de slice-00) no
    /// colisionan entre sí.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>
    /// Crea un turno en <see cref="EstadoTurno.PendientePago"/>. No ocupa puesto.
    /// </summary>
    public static Turno Crear(
        Guid lavaderoId,
        Guid puestoId,
        Guid clienteId,
        Guid tipoLavadoId,
        DateTime inicioUtc,
        DateTime finUtc,
        decimal precioSena,
        OrigenTurno origen = OrigenTurno.App)
    {
        if (precioSena < 0)
        {
            throw new ArgumentException(
                "La seña no puede ser negativa.",
                nameof(precioSena));
        }

        // RangoTurno valida UTC y orden de los extremos, con un mensaje propio.
        return new Turno(
            Guid.NewGuid(), lavaderoId, puestoId, clienteId, tipoLavadoId,
            new RangoTurno(inicioUtc, finUtc), precioSena, origen);
    }

    /// <summary>
    /// Crea el turno de una reserva: agrega el vencimiento del TTL de la seña y
    /// la clave de idempotencia (SPECS/slice-01 T-13). Ambos son datos del
    /// intento de reserva, no de la demanda espontánea, por eso no viven en
    /// <see cref="Crear"/>.
    /// </summary>
    public static Turno CrearParaReserva(
        Guid lavaderoId,
        Guid puestoId,
        Guid clienteId,
        Guid tipoLavadoId,
        DateTime inicioUtc,
        DateTime finUtc,
        decimal precioSena,
        DateTime expiraEnUtc,
        string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var turno = Crear(
            lavaderoId, puestoId, clienteId, tipoLavadoId, inicioUtc, finUtc, precioSena);
        turno.ExpiraEn = expiraEnUtc;
        turno.IdempotencyKey = idempotencyKey;
        return turno;
    }

    /// <summary>
    /// Crea el turno de una demanda espontánea (CU-07): nace directamente en
    /// <see cref="EstadoTurno.EnCurso"/> y ocupa el puesto de inmediato, sin seña
    /// ni comprobante. Es la única fábrica que no pasa por
    /// <see cref="EstadoTurno.PendientePago"/> (SPECS/slice-03, decisiones D1/D9).
    /// </summary>
    public static Turno CrearEspontaneo(
        Guid lavaderoId,
        Guid puestoId,
        Guid clienteId,
        Guid tipoLavadoId,
        DateTime inicioUtc,
        DateTime finUtc)
    {
        var turno = Crear(
            lavaderoId, puestoId, clienteId, tipoLavadoId, inicioUtc, finUtc,
            precioSena: 0m, origen: OrigenTurno.Espontanea);

        // El estado inicial lo fija la fábrica (no TransicionarA, que es la vía
        // de los cambios posteriores). Sin ConfirmadoEn: nunca hubo pago.
        turno.Estado = EstadoTurno.EnCurso;
        return turno;
    }

    /// <summary>
    /// Única forma de cambiar el estado. Toda transición no listada en
    /// ARCHITECTURE §5 lanza <see cref="TurnoTransicionInvalida"/>.
    /// </summary>
    /// <exception cref="TurnoTransicionInvalida">Transición no autorizada por la tabla de §5.</exception>
    public void TransicionarA(EstadoTurno destino)
    {
        if (!Transiciones[Estado].Contains(destino))
        {
            throw new TurnoTransicionInvalida(Estado, destino);
        }

        Estado = destino;

        if (destino == EstadoTurno.Confirmado)
        {
            ConfirmadoEn = DateTime.UtcNow;
        }
        else if (EsTerminal(destino))
        {
            CerradoEn = DateTime.UtcNow;
        }
    }

    /// <summary>Registra la referencia de la preferencia de Mercado Pago.</summary>
    /// <param name="mpPreferenceId">Identificador externo de la preferencia.</param>
    /// <param name="initPoint">
    /// URL de checkout devuelta por la pasarela. Es opcional porque un turno
    /// reconstruido desde la base no la tiene: no se persiste.
    /// </param>
    public void RegistrarPreferenciaDePago(string mpPreferenceId, string? initPoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPreferenceId);

        if (Estado != EstadoTurno.PendientePago)
        {
            throw new InvalidOperationException(
                $"Solo un turno pendiente_pago tiene preferencia de pago; el turno está en {Estado}.");
        }

        MpPreferenceId = mpPreferenceId;
        InitPoint = initPoint;
    }

    /// <summary>
    /// URL de checkout de la pasarela (slice-09, T-95). No se persiste: solo
    /// viaja en la respuesta del alta para que el cliente pueda pagar la seña.
    /// </summary>
    [NotMapped]
    public string? InitPoint { get; private set; }

    /// <summary>
    /// Fija el vencimiento del TTL de la seña. Uso previsto: la fábrica de
    /// reserva y los tests de dominio; un turno ya resuelto no lo modifica.
    /// </summary>
    public void FijarVencimiento(DateTime expiraEnUtc)
    {
        if (expiraEnUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "El vencimiento del turno debe estar en UTC.", nameof(expiraEnUtc));
        }

        ExpiraEn = expiraEnUtc;
    }

    /// <summary>
    /// Indica si el turno ocupa un puesto. Corresponde exactamente al predicado
    /// <c>WHERE (estado IN ('confirmado', 'en_curso'))</c> del EXCLUDE (§4.1).
    /// </summary>
    public bool OcupaPuesto => Estado is EstadoTurno.Confirmado or EstadoTurno.EnCurso;

    /// <summary>
    /// Bloquea el intervalo <b>ahora</b>: la ocupación definitiva más el
    /// <see cref="EstadoTurno.PendientePago"/> todavía vigente
    /// (<c>ExpiraEn &gt; now</c>). Es el espejo de la disponibilidad
    /// (SPECS/slice-01 §3): la consulta usa este criterio con los 3 estados;
    /// el EXCLUDE usa los 3 estados sin la condición temporal, que vive acá.
    /// </summary>
    public bool BloqueaPuestoAhora(DateTime ahoraUtc) =>
        OcupaPuesto ||
        (Estado == EstadoTurno.PendientePago && ExpiraEn is { } vence && vence > ahoraUtc);

    private static bool EsTerminal(EstadoTurno estado) =>
        estado is EstadoTurno.Finalizado or EstadoTurno.Cancelado
            or EstadoTurno.NoPresentado or EstadoTurno.Expirado;
}
