namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Fila de la tabla `outbox` (ARCHITECTURE §4.1, §7.3): evento de ocupación
/// pendiente de publicar por SignalR.
///
/// Se escribe SIEMPRE en el mismo <c>SaveChangesAsync</c> que la escritura de
/// ocupación (RB-2, decisión D3/D12), de modo que nunca hay un cambio de estado
/// sin evento. El publicador (Api) lee las filas con <see cref="PublicadoEn"/>
/// nulo, las envía al grupo del lavadero y las marca (entrega at-least-once).
/// </summary>
public sealed class Outbox
{
    public Guid Id { get; private set; }

    public string Tipo { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTime? PublicadoEn { get; private set; }

    public DateTime CreadoEn { get; private set; }

    private Outbox()
    {
    }

    public static Outbox ParaEvento(string tipo, string payloadJson, DateTime creadoEn) =>
        new()
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Payload = payloadJson,
            PublicadoEn = null,
            CreadoEn = creadoEn,
        };

    /// <summary>
    /// Marca la fila como publicada. La llama el publicador tras enviar el
    /// evento al grupo; es idempotente a nivel de entrega (at-least-once).
    /// </summary>
    public void MarcarPublicado(DateTime ahoraUtc) => PublicadoEn = ahoraUtc;
}
