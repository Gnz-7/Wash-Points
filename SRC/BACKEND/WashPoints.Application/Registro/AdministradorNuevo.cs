namespace WashPoints.Application.Registro;

/// <summary>
/// Datos del alta de un administrador y de su lavadero (slice-08, D-09.3).
///
/// La ubicación es obligatoria porque el lavadero recién creado tiene que
/// aparecer en la búsqueda por proximidad de CU-01; <paramref name="Direccion"/>
/// es opcional.
/// </summary>
public sealed record AdministradorNuevo(
    string Nombre,
    string Usuario,
    string Contrasena,
    string NombreLavadero,
    string? Direccion,
    double? Latitud,
    double? Longitud);
