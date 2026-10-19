namespace WashPoints.Domain.Metricas;

/// <summary>
/// Minutos que un puesto estuvo ocupado dentro de la ventana consultada
/// (CU-09). La tasa se calcula en la capa de aplicación, porque depende de la
/// ventana pedida y no del puesto.
/// </summary>
public sealed record OcupacionPuesto(Guid PuestoId, string Nombre, int MinutosOcupados);
