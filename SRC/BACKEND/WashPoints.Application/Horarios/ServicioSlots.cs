using WashPoints.Application.Agenda;
using WashPoints.Application.Catalogo;
using WashPoints.Application.ReservarTurno;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Tiempo;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Horarios;

/// <summary>
/// Grilla de slots de una fecha y un tipo de lavado (SPECS/slice-17 §4.3,
/// D-17.9, R-1/R-2/R-4).
///
/// Es un endpoint <b>aditivo</b>: no reemplaza la disponibilidad en tiempo real
/// ni el hub de SignalR, que siguen publicando intervalos ocupados. El cliente
/// sólo pinta lo que este servicio oferta; no calcula anticipación, horizonte,
/// horarios ni compatibilidad (D-17.12).
///
/// La grilla se arma por <c>DuracionMinutos</c> desde la apertura de cada
/// franja y se descartan los slots cuyo fin exceda el cierre. Un slot es
/// <c>disponible</c> cuando al menos un puesto activo y compatible está libre
/// en ese intervalo y el inicio respeta la anticipación mínima. Los slots no
/// disponibles se devuelven igual, con <c>motivo</c>, para que la UI los
/// muestre deshabilitados en vez de hacer desaparecer la grilla.
/// </summary>
public sealed class ServicioSlots
{
    /// <summary>Motivo de un slot con todos los puestos compatibles tomados.</summary>
    public const string MotivoOcupado = "ocupado";

    /// <summary>Motivo de un slot demasiado próximo al momento de consultar.</summary>
    public const string MotivoAnticipacion = "anticipacion";

    private readonly IConsultaCatalogo _catalogo;
    private readonly IHorarioRepositorio _horarios;
    private readonly IOcupacionRepositorio _ocupacion;
    private readonly IConversorHorario _conversor;
    private readonly OpcionesAgenda _agenda;
    private readonly TimeProvider _reloj;

    public ServicioSlots(
        IConsultaCatalogo catalogo,
        IHorarioRepositorio horarios,
        IOcupacionRepositorio ocupacion,
        IConversorHorario conversor,
        OpcionesAgenda agenda,
        TimeProvider reloj)
    {
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _horarios = horarios ?? throw new ArgumentNullException(nameof(horarios));
        _ocupacion = ocupacion ?? throw new ArgumentNullException(nameof(ocupacion));
        _conversor = conversor ?? throw new ArgumentNullException(nameof(conversor));
        _agenda = agenda ?? throw new ArgumentNullException(nameof(agenda));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
    }

    /// <summary>
    /// Arma la grilla. Lanza <see cref="LavaderoNoEncontradoException"/> y
    /// <see cref="TipoLavadoNoEncontradoException"/> (404) y
    /// <see cref="FueraDeHorizonteException"/> (422) antes de tocar la
    /// ocupación.
    /// </summary>
    public async Task<GrillaSlots> ObtenerAsync(
        Guid lavaderoId,
        DateOnly fecha,
        Guid tipoLavadoId,
        CancellationToken ct = default)
    {
        var catalogo = await _catalogo.ObtenerCatalogoAsync(lavaderoId, ct)
            ?? throw new LavaderoNoEncontradoException($"No existe el lavadero {lavaderoId}.");

        // El catálogo sólo publica tipos activos del lavadero: un id ajeno o
        // inactivo se trata como inexistente (D-17.8).
        var tipo = catalogo.Tipos.FirstOrDefault(t => t.Id == tipoLavadoId)
            ?? throw new TipoLavadoNoEncontradoException(
                $"No existe el tipo de lavado {tipoLavadoId} en el lavadero {lavaderoId}.");

        var ahoraUtc = _reloj.GetUtcNow().UtcDateTime;
        var hoyLocal = DateOnly.FromDateTime(_conversor.ALocal(ahoraUtc));

        if (fecha < hoyLocal || fecha > hoyLocal.AddDays(_agenda.HorizonteMaximoDias))
        {
            throw new FueraDeHorizonteException(
                $"Sólo se pueden consultar horarios entre hoy y {_agenda.HorizonteMaximoDias} días de anticipación.");
        }

        var franjas = CalendarioAtencion.FranjasDelDia(
            await _horarios.ListarAsync(lavaderoId, ct),
            fecha.ToDateTime(TimeOnly.MinValue));

        if (franjas.Count == 0)
        {
            return new GrillaSlots(
                fecha, tipoLavadoId, tipo.DuracionMinutos, Abierto: false, [], []);
        }

        var puestos = catalogo.Puestos
            .Where(puesto => EsCompatible(catalogo, puesto.Id, tipoLavadoId))
            .ToArray();

        // ListarDelDiaAsync filtra por el inicio del turno en el día local; una
        // franja no cruza la medianoche (D-17.3), así que alcanza para cubrir
        // los solapamientos de la grilla.
        var desdeUtc = _conversor.AUtc(fecha.ToDateTime(TimeOnly.MinValue));
        var hastaUtc = _conversor.AUtc(fecha.AddDays(1).ToDateTime(TimeOnly.MinValue));

        // Los pendientes de pago vigentes también ocupan puesto: es el mismo
        // predicado de la disponibilidad en tiempo real (RB-2). El filtro por
        // expiración es necesario porque ListarDelDiaAsync no lo aplica.
        var turnos = (await _ocupacion.ListarDelDiaAsync(lavaderoId, desdeUtc, hastaUtc, ct))
            .Where(turno => turno.BloqueaPuestoAhora(ahoraUtc))
            .ToArray();

        var limiteAnticipacion = ahoraUtc.AddMinutes(_agenda.AnticipacionMinimaMinutos);
        var slots = new List<SlotCalculado>();

        foreach (var franja in franjas)
        {
            for (var inicio = franja.Apertura;
                 inicio.AddMinutes(tipo.DuracionMinutos) <= franja.Cierre;
                 inicio = inicio.AddMinutes(tipo.DuracionMinutos))
            {
                var fin = inicio.AddMinutes(tipo.DuracionMinutos);
                var inicioUtc = _conversor.AUtc(fecha.ToDateTime(inicio));
                var finUtc = _conversor.AUtc(fecha.ToDateTime(fin));

                var libres = puestos.Count(puesto => !turnos.Any(turno =>
                    turno.PuestoId == puesto.Id
                    && turno.InicioUtc < finUtc
                    && inicioUtc < turno.FinUtc));

                var respetaAnticipacion = inicioUtc >= limiteAnticipacion;
                var disponible = respetaAnticipacion && libres > 0;

                slots.Add(new SlotCalculado(
                    inicio,
                    fin,
                    disponible,
                    libres,
                    disponible
                        ? null
                        : respetaAnticipacion ? MotivoOcupado : MotivoAnticipacion));
            }
        }

        return new GrillaSlots(
            fecha,
            tipoLavadoId,
            tipo.DuracionMinutos,
            Abierto: true,
            franjas.Select(franja => new FranjaSlot(franja.Apertura, franja.Cierre)).ToArray(),
            slots);
    }

    /// <summary>
    /// Sin filas de compatibilidad el puesto atiende todos los servicios
    /// (D-17.7); con filas, sólo los tipos listados.
    /// </summary>
    private static bool EsCompatible(
        CatalogoLavadero catalogo, Guid puestoId, Guid tipoLavadoId)
    {
        var tipos = catalogo.TiposDe(puestoId);

        return tipos.Count == 0 || tipos.Contains(tipoLavadoId);
    }
}
