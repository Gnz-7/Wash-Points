namespace WashPoints.Application.Registro;

/// <summary>
/// Datos del alta de un cliente conductor (slice-08, D-09.2).
///
/// No incluye teléfono ni patente: la minimización de PII de AGENTS §3.4
/// sigue vigente y el contacto queda diferido (D-09.1, ARCHITECTURE §10.7).
/// </summary>
public sealed record ClienteNuevo(string Nombre, string Usuario, string Contrasena);
