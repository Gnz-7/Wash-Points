using System.Security.Cryptography;
using System.Text;
using WashPoints.Application.Agenda;
using WashPoints.Application.Pagos;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.MercadoPago;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Tiempo;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.ReservarTurno;

/// <summary>
/// Orquesta la reserva con seña (SPECS/slice-01 T-13, T-14).
///
/// Secuencia: crea el turno <c>pendiente_pago</c> con vencimiento y clave de
/// idempotencia, inserta el turno y pide la preferencia a la pasarela dentro de
/// una transacción. Si la pasarela falla, la transacción se revierte: no queda
/// ni turno huérfano ni preferencia sin turno (CA-S1.3). Si el INSERT choca por
/// exclusión (23P01) o unicidad de clave (23505), reconsulta por la clave y
/// devuelve el turno original (CA-S1.2); si la clave no existe, otro cliente
/// ganó el slot y se propaga <see cref="TurnoConflictoException"/>.
///
/// Sobre el orden INSERT→preferencia: se persiste el turno primero porque la
/// EXCLUDE debe arbitrar el slot antes de tocar la pasarela; así un reintento
/// idéntico nunca crea una segunda preferencia.
/// </summary>
public sealed class ReservaServicio
{
    private readonly IOcupacionRepositorio _repositorio;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly IConsultaCatalogo _catalogo;
    private readonly IOutbox _outbox;
    private readonly IPasarelaPagos _pasarela;
    private readonly IObtenedorTokensMp _tokensMp;
    private readonly OpcionesPago _opciones;
    private readonly TimeProvider _reloj;
    private readonly IConversorHorario _conversor;
    private readonly IHorarioRepositorio _horarios;
    private readonly OpcionesAgenda _agenda;

    public ReservaServicio(
        IOcupacionRepositorio repositorio,
        IUnidadDeTrabajo unidadDeTrabajo,
        IConsultaCatalogo catalogo,
        IOutbox outbox,
        IPasarelaPagos pasarela,
        IObtenedorTokensMp tokensMp,
        OpcionesPago opciones,
        TimeProvider reloj,
        IConversorHorario conversor,
        IHorarioRepositorio horarios,
        OpcionesAgenda agenda)
    {
        _repositorio = repositorio;
        _unidadDeTrabajo = unidadDeTrabajo;
        _catalogo = catalogo;
        _outbox = outbox;
        _pasarela = pasarela;
        _tokensMp = tokensMp;
        _opciones = opciones;
        _reloj = reloj;
        _conversor = conversor;
        _horarios = horarios;
        _agenda = agenda;
    }

    public async Task<Turno> ReservarTurnoAsync(SolicitudReserva solicitud, CancellationToken ct)
    {
        // Orden de validación D-17.5: pertenencia → ventana temporal → horario
        // → compatibilidad. Todo rechazo ocurre antes de crear el turno y de
        // tocar la pasarela: nunca se pide una preferencia de pago por una
        // reserva inválida (refuerza RB-1).
        var tipo = await _catalogo.ObtenerTipoLavadoAsync(
                solicitud.LavaderoId, solicitud.TipoLavadoId, ct)
            ?? throw new TipoLavadoNoEncontradoException(
                $"No existe el tipo de lavado {solicitud.TipoLavadoId} en el lavadero {solicitud.LavaderoId}.");

        _ = await _catalogo.ObtenerPuestoAsync(
                solicitud.LavaderoId, solicitud.PuestoId, ct)
            ?? throw new PuestoNoEncontradoException(
                $"No existe el puesto {solicitud.PuestoId} en el lavadero {solicitud.LavaderoId}.");

        var ahoraUtc = _reloj.GetUtcNow().UtcDateTime;

        if (solicitud.InicioUtc < ahoraUtc.AddMinutes(_agenda.AnticipacionMinimaMinutos))
        {
            throw new AnticipacionInsuficienteException(
                $"El turno debe reservarse con al menos {_agenda.AnticipacionMinimaMinutos} minutos de anticipación.");
        }

        if (solicitud.InicioUtc > ahoraUtc.AddDays(_agenda.HorizonteMaximoDias))
        {
            throw new FueraDeHorizonteException(
                $"Sólo se puede reservar hasta {_agenda.HorizonteMaximoDias} días de anticipación.");
        }

        var finUtc = solicitud.InicioUtc.AddMinutes(tipo.DuracionMinutos);
        var franjas = await _horarios.ListarAsync(solicitud.LavaderoId, ct);

        if (!CalendarioAtencion.EstaAbierto(
                franjas, _conversor.ALocal(solicitud.InicioUtc), _conversor.ALocal(finUtc)))
        {
            throw new FueraDeHorarioAtencionException(
                "El turno no entra completo en el horario de atención del lavadero.");
        }

        if (!await _catalogo.PuestoAtiendeTipoAsync(
                solicitud.LavaderoId, solicitud.PuestoId, solicitud.TipoLavadoId, ct))
        {
            throw new PuestoIncompatibleException(
                "El puesto no atiende el tipo de lavado elegido.");
        }

        // slice-20 (D-20.4): el cobro es un Checkout Pro de la cuenta del
        // lavadero. Sin vínculo, ObtenerTokenVigenteAsync lanza
        // MercadoPagoNoVinculadoException (422 mercadopago_no_vinculado) antes
        // de crear el turno o tocar la pasarela (RB-1).
        var accessToken = await _tokensMp.ObtenerTokenVigenteAsync(solicitud.LavaderoId, ct);

        try
        {
            return await ReservarUnaVezAsync(solicitud, accessToken, tipo, finUtc, ct);
        }
        catch (PasarelaTokenInvalidoException)
        {
            // El token del lavadero expiró o fue revocado: se refresca una
            // sola vez y se reintenta. La transacción del primer intento ya
            // revirtió el INSERT y la clave de idempotencia hace equivalente
            // al reintento (CA-S1.2).
            var fresco = await _tokensMp.RefrescarTokenAsync(solicitud.LavaderoId, ct);
            return await ReservarUnaVezAsync(solicitud, fresco, tipo, finUtc, ct);
        }
    }

    /// <summary>
    /// Un intento completo de reserva: turno + outbox + preferencia dentro de
    /// una transacción. Se llama dos veces a lo sumo (token refrescado).
    /// </summary>
    private async Task<Turno> ReservarUnaVezAsync(
        SolicitudReserva solicitud,
        string accessToken,
        TipoLavado tipo,
        DateTime finUtc,
        CancellationToken ct)
    {
        var ahoraUtc = _reloj.GetUtcNow().UtcDateTime;
        var idempotencyKey = CalcularIdempotencyKey(solicitud, finUtc);
        var expiraEn = ahoraUtc.AddMinutes(_opciones.TtlReservaMinutos);

        var turno = Turno.CrearParaReserva(
            solicitud.LavaderoId,
            solicitud.PuestoId,
            solicitud.ClienteId,
            solicitud.TipoLavadoId,
            solicitud.InicioUtc,
            finUtc,
            tipo.PrecioSena,
            tipo.PrecioTotal,
            expiraEn,
            idempotencyKey);

        try
        {
            return await _unidadDeTrabajo.EjecutarEnTransaccionAsync(async ctTransaccion =>
            {
                // 1) Insertar el turno: la EXCLUDE decide el slot acá.
                await _repositorio.AgregarAsync(turno, ctTransaccion);

                // RB-2 (SPECS/slice-04 §5.3): la creación bloquea el puesto (el
                // pendiente_pago vigente ocupa), así que encola ocupación y
                // agenda. Viajan en el mismo flush que el turno (D-04.10).
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

                await _repositorio.GuardarAsync(ctTransaccion);

                // 2) Preferencia de pago. Si falla, la transacción revierte el
                //    INSERT: ni turno ni preferencia quedan.
                var preferencia = new PreferenciaPago(
                    TurnoId: turno.Id,
                    Monto: tipo.PrecioSena,
                    Descripcion: $"Seña {tipo.Nombre}",
                    ReferenciaExterna: turno.Id.ToString(),
                    // La web vuelve a una ruta propia con el turno en la query:
                    // así la app verifica el pago en caliente al regresar del
                    // checkout (el navegador nunca confirma nada por sí solo).
                    UrlRetorno: ConstruirUrlRetorno(turno.Id));
                var creada = await _pasarela.CrearPreferenciaAsync(preferencia, accessToken, ctTransaccion);

                turno.RegistrarPreferenciaDePago(creada.PreferenceId, creada.InitPoint);
                await _repositorio.GuardarAsync(ctTransaccion);

                return turno;
            }, ct);
        }
        catch (TurnoConflictoException)
        {
            return await RecuperarPorClaveOPropagarAsync(idempotencyKey, ct);
        }
        catch (ReservaDuplicadaException)
        {
            return await RecuperarPorClaveOPropagarAsync(idempotencyKey, ct);
        }
    }

    /// <summary>
    /// URL de retorno del checkout:
    /// <c>{UrlWeb}/turnos/{id}/comprobante?origen=mp</c>. Sin
    /// <see cref="OpcionesPago.UrlWeb"/> devuelve <c>null</c> y el adaptador usa
    /// su URL por defecto. La query es sólo una pista de navegación: el pago se
    /// confirma contra la pasarela, nunca por el retorno del navegador (RB-1).
    /// </summary>
    private string? ConstruirUrlRetorno(Guid turnoId)
    {
        if (string.IsNullOrWhiteSpace(_opciones.UrlWeb))
        {
            return null;
        }

        return $"{_opciones.UrlWeb.TrimEnd('/')}/turnos/{turnoId}/comprobante?origen=mp";
    }

    /// <summary>
    /// Reintento idéntico: devuelve el turno original. Si la clave no aparece,
    /// el slot lo ganó otra reserva → conflicto (CA-S1.2 / CA-S1.9).
    /// </summary>
    private async Task<Turno> RecuperarPorClaveOPropagarAsync(string idempotencyKey, CancellationToken ct)
    {
        var original = await _repositorio.ObtenerPorIdempotencyKeyAsync(idempotencyKey, ct);
        if (original is not null)
        {
            return original;
        }

        throw new TurnoConflictoException(
            "El puesto ya tiene una reserva en ese intervalo.");
    }

    /// <summary>
    /// SHA-256 de <c>(clienteId, puestoId, tipoLavadoId, inicioUtc, finUtc)</c>
    /// (SPECS/slice-01 §3). Determinista: el mismo intento produce la misma clave.
    /// </summary>
    private static string CalcularIdempotencyKey(SolicitudReserva solicitud, DateTime finUtc)
    {
        var material = string.Join(
            '|',
            solicitud.ClienteId.ToString("N"),
            solicitud.PuestoId.ToString("N"),
            solicitud.TipoLavadoId.ToString("N"),
            solicitud.InicioUtc.ToString("O"),
            finUtc.ToString("O"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
