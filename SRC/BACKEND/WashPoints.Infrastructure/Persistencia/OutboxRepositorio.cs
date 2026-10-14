using System.Text.Json;
using System.Text.Json.Serialization;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de outbox (RB-2, ARCHITECTURE §7.3).
///
/// Respeta el contrato D12: <see cref="Encolar"/> solo agrega la fila al Change
/// Tracker; no llama a <c>SaveChangesAsync</c>. El commit lo hace la unidad de
/// trabajo que envuelve la operación (SPECS/slice-03 T-36/T-37).
/// </summary>
public sealed class OutboxRepositorio : IOutbox
{
    /// <summary>Nombre del evento de ocupación (D11). Espeja el de Application.</summary>
    private const string TipoEventoOcupacion = "ocupacion_cambiada";

    /// <summary>Nombre del evento de agenda (D-04.5).</summary>
    private const string TipoEventoAgenda = "agenda_cambiada";

    // Payload legible e inspeccionable: camelCase y enums en snake_case, para
    // que `outbox.payload` sea consistente con el contrato de §7.3 (D11).
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly WashPointsDbContext _contexto;
    private readonly TimeProvider _reloj;

    public OutboxRepositorio(WashPointsDbContext contexto, TimeProvider reloj)
    {
        _contexto = contexto;
        _reloj = reloj;
    }

    public void Encolar(EventoOcupacion evento) =>
        Agregar(TipoEventoOcupacion, evento);

    public void Encolar(EventoAgenda evento) =>
        Agregar(TipoEventoAgenda, evento);

    private void Agregar<T>(string tipo, T evento)
    {
        var payload = JsonSerializer.Serialize(evento, OpcionesJson);
        var creadoEn = _reloj.GetUtcNow().UtcDateTime;

        _contexto.Outbox.Add(Outbox.ParaEvento(tipo, payload, creadoEn));
    }
}
