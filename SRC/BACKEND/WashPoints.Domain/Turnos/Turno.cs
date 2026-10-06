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
            },
            [EstadoTurno.Confirmado] = new HashSet<EstadoTurno>
            {
                EstadoTurno.EnCurso,      // canje del comprobante (RB-3)
                EstadoTurno.NoPresentado, // venció la ventana de llegada
            },
            [EstadoTurno.EnCurso] = new HashSet<EstadoTurno>
            {
                EstadoTurno.Finalizado,
            },
            // Estados terminales: sin transiciones de salida.
            [EstadoTurno.Finalizado] = new HashSet<EstadoTurno>(),
            [EstadoTurno.Cancelado] = new HashSet<EstadoTurno>(),
            [EstadoTurno.NoPresentado] = new HashSet<EstadoTurno>(),
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
    public void RegistrarPreferenciaDePago(string mpPreferenceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPreferenceId);

        if (Estado != EstadoTurno.PendientePago)
        {
            throw new InvalidOperationException(
                $"Solo un turno pendiente_pago tiene preferencia de pago; el turno está en {Estado}.");
        }

        MpPreferenceId = mpPreferenceId;
    }

    /// <summary>
    /// Indica si el turno ocupa un puesto. Corresponde exactamente al predicado
    /// <c>WHERE (estado IN ('confirmado', 'en_curso'))</c> del EXCLUDE (§4.1).
    /// </summary>
    public bool OcupaPuesto => Estado is EstadoTurno.Confirmado or EstadoTurno.EnCurso;

    private static bool EsTerminal(EstadoTurno estado) =>
        estado is EstadoTurno.Finalizado or EstadoTurno.Cancelado or EstadoTurno.NoPresentado;
}
