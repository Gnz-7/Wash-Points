namespace WashPoints.Application.Reembolsos;

/// <summary>
/// Resumen de un tick del worker de reembolsos (SPECS/slice-15 D-15.4).
/// </summary>
/// <param name="Procesados">Reintegros confirmados por la pasarela.</param>
/// <param name="Reintentados">Reintegros que volverán a intentarse con backoff.</param>
/// <param name="Fallidos">Reintegros cerrados sin éxito.</param>
public sealed record ResultadoProcesamientoReembolsos(
    int Procesados,
    int Reintentados,
    int Fallidos);
