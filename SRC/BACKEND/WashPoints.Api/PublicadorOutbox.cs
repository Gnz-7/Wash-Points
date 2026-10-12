using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WashPoints.Api.Hubs;
using WashPoints.Domain.Ocupacion;
using WashPoints.Infrastructure.Persistencia;

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
/// </summary>
public sealed class PublicadorOutbox
{
    /// <summary>Evento servidor→cliente; el mismo nombre que documenta ARCHITECTURE §7.3.</summary>
    public const string Evento = "disponibilidadActualizada";

    // El payload se serializó con las mismas opciones al encolar (camelCase +
    // enums snake_case); hay que deserializarlo con ellas para leer el lavadero.
    private static readonly JsonSerializerOptions OpcionesPayload = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly WashPointsDbContext _contexto;
    private readonly IHubContext<DisponibilidadHub> _hub;
    private readonly TimeProvider _reloj;

    public PublicadorOutbox(
        WashPointsDbContext contexto,
        IHubContext<DisponibilidadHub> hub,
        TimeProvider reloj)
    {
        _contexto = contexto;
        _hub = hub;
        _reloj = reloj;
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
            var evento = JsonSerializer.Deserialize<EventoOcupacion>(fila.Payload, OpcionesPayload)
                ?? throw new InvalidOperationException($"Payload de outbox inválido en la fila {fila.Id}.");

            await _hub.Clients
                .Group(DisponibilidadHub.GrupoDe(evento.LavaderoId))
                .SendAsync(Evento, fila.Payload, ct);

            fila.MarcarPublicado(ahora);
        }

        await _contexto.SaveChangesAsync(ct);
        return pendientes.Count;
    }
}
