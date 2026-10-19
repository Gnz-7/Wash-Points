namespace WashPoints.Application.Metricas;

/// <summary>
/// Ocupación por puesto sobre la ventana consultada (CU-09). Se informa la
/// ventana junto a la tasa para que el porcentaje sea interpretable.
/// </summary>
public sealed record OcupacionDelDia(
    DateTime DesdeUtc,
    DateTime HastaUtc,
    IReadOnlyList<OcupacionDePuesto> Puestos);

/// <summary>
/// Tasa de ocupación de un puesto: minutos ocupados sobre minutos de ventana,
/// recortando cada turno a la ventana (CA-S6.6).
/// </summary>
public sealed record OcupacionDePuesto(
    Guid PuestoId,
    string Nombre,
    int MinutosOcupados,
    int MinutosVentana,
    decimal Tasa);
