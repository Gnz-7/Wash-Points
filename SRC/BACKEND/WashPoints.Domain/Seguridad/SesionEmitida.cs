namespace WashPoints.Domain.Seguridad;

/// <summary>
/// Sesión emitida por <see cref="IEmisorToken"/> (D-05.2): la credencial que el
/// cliente reenvía en <c>Authorization: Bearer</c>.
/// </summary>
public sealed record SesionEmitida(string Token, DateTime ExpiraEnUtc);
