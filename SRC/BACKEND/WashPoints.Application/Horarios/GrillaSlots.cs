namespace WashPoints.Application.Horarios;

/// <summary>Franja de atención publicada en la grilla de slots.</summary>
public sealed record FranjaSlot(TimeOnly Apertura, TimeOnly Cierre);

/// <summary>
/// Un horario ofertado de la grilla (SPECS/slice-17 §4.3, D-17.9).
/// </summary>
/// <param name="Inicio">Hora local de inicio del slot.</param>
/// <param name="Fin">Hora local de fin del slot.</param>
/// <param name="Disponible">
/// Hay al menos un puesto compatible libre y el inicio respeta la anticipación.
/// </param>
/// <param name="PuestosLibres">Puestos activos y compatibles sin turno que solape.</param>
/// <param name="Motivo"><c>ocupado</c>, <c>anticipacion</c> o <c>null</c> si está disponible.</param>
public sealed record SlotCalculado(
    TimeOnly Inicio,
    TimeOnly Fin,
    bool Disponible,
    int PuestosLibres,
    string? Motivo);

/// <summary>
/// Grilla de slots de un lavadero para una fecha local y un tipo de lavado
/// (SPECS/slice-17 §4.3, D-17.9).
///
/// Un día sin franjas no es un error: viaja con <see cref="Abierto"/> en
/// <c>false</c> y listas vacías para que la UI muestre «cerrado ese día».
/// </summary>
public sealed record GrillaSlots(
    DateOnly Fecha,
    Guid TipoLavadoId,
    int DuracionMinutos,
    bool Abierto,
    IReadOnlyList<FranjaSlot> Franjas,
    IReadOnlyList<SlotCalculado> Slots);
