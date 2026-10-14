using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Espontanea;

/// <summary>
/// Registra una demanda espontánea (CU-07, SPECS/slice-03 T-36).
///
/// A diferencia de la reserva, no pide seña ni emite comprobante: el turno nace
/// directamente en <see cref="EstadoTurno.EnCurso"/> y ocupa el puesto (D1/D9).
/// La ocupación y su evento de outbox se persisten en un <b>único</b> commit
/// (D12), de modo que nunca hay un cambio de disponibilidad sin evento.
///
/// El solapamiento lo arbitra la EXCLUDE de la base al guardar: se propaga
/// <see cref="TurnoConflictoException"/> y el endpoint lo mapea a 409.
/// </summary>
public sealed class RegistrarDemandaEspontaneaServicio
{
    /// <summary>Nombre del evento de outbox para un cambio de ocupación (D11).</summary>
    public const string TipoEventoOcupacion = "ocupacion_cambiada";

    /// <summary>
    /// D8 (provisorio): `turnos.cliente_id` es NOT NULL y la demanda espontánea
    /// no tiene cliente real. Se usa un cliente anónimo; se formaliza en
    /// DOCS/ARCHITECTURE.md §10.7.
    /// </summary>
    private const string NombreClienteEspontaneo = "Cliente espontáneo";

    private readonly IOcupacionRepositorio _repositorio;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly IConsultaCatalogo _catalogo;
    private readonly IRegistroClientes _registroClientes;
    private readonly IOutbox _outbox;

    public RegistrarDemandaEspontaneaServicio(
        IOcupacionRepositorio repositorio,
        IUnidadDeTrabajo unidadDeTrabajo,
        IConsultaCatalogo catalogo,
        IRegistroClientes registroClientes,
        IOutbox outbox)
    {
        _repositorio = repositorio;
        _unidadDeTrabajo = unidadDeTrabajo;
        _catalogo = catalogo;
        _registroClientes = registroClientes;
        _outbox = outbox;
    }

    public async Task<Turno> RegistrarAsync(SolicitudEspontanea solicitud, CancellationToken ct)
    {
        var tipo = await _catalogo.ObtenerTipoLavadoAsync(solicitud.TipoLavadoId, ct)
            ?? throw new ArgumentException(
                $"No existe el tipo de lavado {solicitud.TipoLavadoId}.", nameof(solicitud));

        if (tipo.LavaderoId != solicitud.LavaderoId)
        {
            throw new ArgumentException(
                "El tipo de lavado no pertenece al lavadero indicado.", nameof(solicitud));
        }

        var finUtc = solicitud.InicioUtc.AddMinutes(tipo.DuracionMinutos);
        var cliente = Cliente.Crear(NombreClienteEspontaneo);

        var turno = Turno.CrearEspontaneo(
            solicitud.LavaderoId,
            solicitud.PuestoId,
            cliente.Id,
            solicitud.TipoLavadoId,
            solicitud.InicioUtc,
            finUtc);

        return await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async ctTransaccion =>
        {
            await _registroClientes.AgregarAsync(cliente, ctTransaccion);
            await _repositorio.AgregarAsync(turno, ctTransaccion);

            _outbox.Encolar(new EventoOcupacion(
                turno.Id,
                turno.LavaderoId,
                turno.PuestoId,
                turno.Origen,
                turno.Estado,
                turno.InicioUtc,
                turno.FinUtc));
            _outbox.Encolar(new EventoAgenda(
                turno.Id,
                turno.LavaderoId,
                turno.PuestoId,
                turno.Origen,
                turno.Estado,
                turno.InicioUtc,
                turno.FinUtc));

            // Único flush: cliente + turno + outbox en la misma transacción (D12).
            await _repositorio.GuardarAsync(ctTransaccion);

            return turno;
        }, ct);
    }
}
