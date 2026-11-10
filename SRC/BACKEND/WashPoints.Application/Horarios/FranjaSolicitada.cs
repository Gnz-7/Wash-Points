namespace WashPoints.Application.Horarios;

/// <summary>
/// Franja de atención tal como llega en el <c>PUT</c> del panel
/// (SPECS/slice-17 §4.2, T-178).
///
/// Las horas viajan como texto porque el input es del administrador y puede
/// venir mal: la validación y el parseo ocurren en el servicio, no en el
/// endpoint, para que el error sea el mismo por cualquier vía.
/// </summary>
public sealed record FranjaSolicitada(int DiaSemana, string Apertura, string Cierre);
