using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WashPoints.Api.Hubs;
using WashPoints.Domain.Ocupacion;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Api;

/// <summary>
/// Publicador de la outbox (RB-2, ARCHITECTURE §7.3): toma las filas de
/// <c>outbox</c> no publicadas, las emite al grupo del lavadero por SignalR y
/// las marca. Es la mitad que cierra el ciclo "cambio de estado atómico →
/// notificación en vivo".
///
/// <para>
/// <b>Entrega at-least-once.</b> La fila se marca recién después de enviar; si
/// el proceso cae entre el envío y el <c>SaveChangesAsync</c>, el evento se
/// reenvía en el próximo tick. El cliente debe tolerar duplicados; el evento
/// lleva el estado completo, no un delta.
/// </para>
///
/// <para>
/// <b>Sin lógica de negocio.</b> Solo bombea lo que ya está persistido: la
/// fuente de verdad es la tabla <c>turnos</c>, no el evento (RB-2).
/// </para>
///
/// <para>
/// <b>Canal administrativo de métricas (slice-18).</b> Además del envío
/// primario, cada fila deriva un aviso <c>metricasActualizadas</c> al grupo
/// <c>metricas-{id}</c>. Esa pata es <i>best-effort</i>: si falla, se registra
/// y el lote sigue, de modo que el canal de métricas nunca puede degradar la
/// disponibilidad ni la agenda (D-18.4).
/// </para>
/// </summary>
public sealed class PublicadorOutbox
{
    /// <summary>Evento servidor→cliente; el mismo nombre que documenta ARCHITECTURE §7.3.</summary>
    public const string Evento = "disponibilidadActualizada";

    /// <summary>Evento servidor→cliente del canal de agenda (D-04.5, slice-04 T-48).</summary>
    public const string EventoAgenda = "agendaActualizada";

    /// <summary>
    /// Evento servidor→cliente del canal de métricas (slice-18, D-18.2). Es una
    /// señal de invalidación: el panel refetchea por REST.
    /// </summary>
    public const string EventoMetricas = "metricasActualizadas";

    // Tipos de evento persistidos en la columna outbox.tipo.
    private const string TipoOcupacion = "ocupacion_cambiada";
    private const string TipoAgenda = "agenda_cambiada";
    private const string TipoMetricas = "metricas_cambiada";

    // El payload se serializó con las mismas opciones al encolar (camelCase +
    // enums snake_case); hay que deserializarlo con ellas para leer el lavadero.
    private static readonly JsonSerializerOptions OpcionesPayload = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly WashPointsDbContext _contexto;
    private readonly IHubContext<DisponibilidadHub> _hub;
    private readonly TimeProvider _reloj;
    private readonly ILogger<PublicadorOutbox> _log;

    public PublicadorOutbox(
        WashPointsDbContext contexto,
        IHubContext<DisponibilidadHub> hub,
        TimeProvider reloj,
        ILogger<PublicadorOutbox> log)
    {
        _contexto = contexto;
        _hub = hub;
        _reloj = reloj;
        _log = log;
    }

    /// <summary>
    /// Publica un lote de eventos pendientes y devuelve cuántos envió. El lote
    /// está acotado para no retener el <c>DbContext</c> ni el lock de fila ante
    /// un backlog grande.
    /// </summary>
    public async Task<int> PublicarPendientesAsync(CancellationToken ct)
    {
        var pendientes = await _contexto.Outbox
            .Where(o => o.PublicadoEn == null)
            .OrderBy(o => o.CreadoEn)
            .Take(100)
            .ToListAsync(ct);

        if (pendientes.Count == 0)
        {
            return 0;
        }

        var ahora = _reloj.GetUtcNow().UtcDateTime;

        foreach (var fila in pendientes)
        {
            // El ruteo depende del tipo de evento (RB-2, D-04.5): ocupación va al
            // grupo de disponibilidad; agenda, al grupo de agenda. Ambos canales
            // llevan el payload completo ya serializado al encolar. El aviso de
            // métricas se deriva de esas mismas filas (slice-18, D-18.3).
            Guid lavaderoId;
            DateOnly? fecha;

            switch (fila.Tipo)
            {
                case TipoOcupacion:
                {
                    var evento = JsonSerializer.Deserialize<EventoOcupacion>(fila.Payload, OpcionesPayload)
                        ?? throw new InvalidOperationException($"Payload de outbox inválido en la fila {fila.Id}.");

                    await _hub.Clients
                        .Group(DisponibilidadHub.GrupoDe(evento.LavaderoId))
                        .SendAsync(Evento, fila.Payload, ct);

                    lavaderoId = evento.LavaderoId;
                    fecha = DateOnly.FromDateTime(ConversorHorario.ALocal(evento.DesdeUtc));
                    break;
                }
                case TipoAgenda:
                {
                    var evento = JsonSerializer.Deserialize<EventoAgenda>(fila.Payload, OpcionesPayload)
                        ?? throw new InvalidOperationException($"Payload de outbox inválido en la fila {fila.Id}.");

                    await _hub.Clients
                        .Group(DisponibilidadHub.GrupoAgendaDe(evento.LavaderoId))
                        .SendAsync(EventoAgenda, fila.Payload, ct);

                    lavaderoId = evento.LavaderoId;
                    fecha = DateOnly.FromDateTime(ConversorHorario.ALocal(evento.InicioUtc));
                    break;
                }
                case TipoMetricas:
                {
                    // Cambio de capacidad: no hay evento de turno del que
                    // derivarlo, así que se encola explícitamente (D-18.5).
                    var evento = JsonSerializer.Deserialize<EventoMetricas>(fila.Payload, OpcionesPayload)
                        ?? throw new InvalidOperationException($"Payload de outbox inválido en la fila {fila.Id}.");

                    await EnviarMetricasAsync(evento.LavaderoId, evento.Fecha, ahora, ct);
                    fila.MarcarPublicado(ahora);
                    continue;
                }
                default:
                    // Tipo desconocido: se marca como publicado sin emitir para
                    // no bloquear la cola ante un emisor más nuevo.
                    fila.MarcarPublicado(ahora);
                    continue;
            }

            // Fan-out best-effort: un fallo del canal administrativo no puede
            // tumbar el lote ni degradar RB-2 (D-18.4).
            await EnviarMetricasAsync(lavaderoId, fecha, ahora, ct);
            fila.MarcarPublicado(ahora);
        }

        await _contexto.SaveChangesAsync(ct);
        return pendientes.Count;
    }

    /// <summary>
    /// Emite <c>metricasActualizadas</c> al grupo administrativo del lavadero.
    /// Es best-effort: el error se registra y no se propaga, para que un
    /// problema del canal de métricas nunca impida publicar disponibilidad ni
    /// agenda. La cancelación del lote sí se propaga.
    /// </summary>
    private async Task EnviarMetricasAsync(
        Guid lavaderoId,
        DateOnly? fecha,
        DateTime ahora,
        CancellationToken ct)
    {
        try
        {
            await _hub.Clients
                .Group(DisponibilidadHub.GrupoMetricasDe(lavaderoId))
                .SendAsync(EventoMetricas, new EventoMetricas(lavaderoId, fecha, ahora), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex,
                "No se pudo publicar {Evento} para el lavadero {LavaderoId}.",
                EventoMetricas,
                lavaderoId);
        }
    }
}
