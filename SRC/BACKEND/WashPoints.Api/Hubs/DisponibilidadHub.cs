using Microsoft.AspNetCore.SignalR;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Api.Hubs;

/// <summary>
/// Hub de disponibilidad en tiempo real (RB-2, ARCHITECTURE §7.3).
///
/// Cada lavadero tiene su propio grupo <c>lavadero-{id}</c>: un cliente se une
/// al abrir la ficha del lavadero y solo recibe los cambios de ese local. El
/// evento servidor→cliente es <c>disponibilidadActualizada</c> y lo emite el
/// publicador de la outbox, no el Hub.
///
/// <see cref="SolicitarSnapshot"/> cubre CA-5.3 (versión backend): devuelve la
/// ocupación vigente en hora local, tomada de la misma consulta que alimenta el
/// endpoint REST (una sola fuente de verdad para reservas y espontáneas).
/// </summary>
public sealed class DisponibilidadHub : Hub
{
    private readonly IDisponibilidadConsulta _consulta;

    public DisponibilidadHub(IDisponibilidadConsulta consulta) => _consulta = consulta;

    /// <summary>Nombre del grupo SignalR de un lavadero. Único para todos los emisores.</summary>
    public static string GrupoDe(Guid lavaderoId) => $"lavadero-{lavaderoId}";

    /// <summary>Suscribe la conexión a los cambios de ocupación del lavadero.</summary>
    public Task UnirseALavadero(Guid lavaderoId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GrupoDe(lavaderoId));

    /// <summary>Da de baja la conexión del grupo del lavadero.</summary>
    public Task SalirDeLavadero(Guid lavaderoId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GrupoDe(lavaderoId));

    /// <summary>
    /// Ocupación vigente del lavadero en el rango pedido. El cliente piensa en
    /// hora local; el contrato del dominio, en UTC (§10.6).
    /// </summary>
    public async Task<IReadOnlyList<DisponibilidadItem>> SolicitarSnapshot(
        Guid lavaderoId,
        DateTime desdeLocal,
        DateTime hastaLocal)
    {
        var ocupados = await _consulta.ObtenerOcupadosAsync(
            lavaderoId,
            ConversorHorario.AUtc(desdeLocal),
            ConversorHorario.AUtc(hastaLocal),
            Context.ConnectionAborted);

        return ocupados.Select(i => new DisponibilidadItem(
            i.TurnoId,
            i.PuestoId,
            i.Origen == OrigenTurno.App ? "app" : "espontanea",
            ConversorHorario.ALocal(i.InicioUtc),
            ConversorHorario.ALocal(i.FinUtc))).ToArray();
    }
}

/// <summary>Intervalo ocupado en hora local, tal como lo recibe el cliente (CA-5.3).</summary>
public sealed record DisponibilidadItem(
    Guid TurnoId,
    Guid PuestoId,
    string Origen,
    DateTime Desde,
    DateTime Hasta);
