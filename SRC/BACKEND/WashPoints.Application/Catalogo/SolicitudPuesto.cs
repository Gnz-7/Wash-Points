namespace WashPoints.Application.Catalogo;

/// <summary>
/// Solicitud de creación de un puesto de lavado (CU-05, SPECS/slice-04 T-44).
/// </summary>
public sealed record SolicitudPuesto(Guid LavaderoId, string Nombre);
