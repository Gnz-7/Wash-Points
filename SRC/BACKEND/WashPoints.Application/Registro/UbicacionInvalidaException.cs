namespace WashPoints.Application.Registro;

/// <summary>
/// La ubicación del lavadero falta o está fuera de rango (slice-08, §5.4).
///
/// Comparte nombre con <see cref="WashPoints.Application.Geo.UbicacionInvalidaException"/>
/// porque son códigos de error distintos en flujos distintos; el endpoint de
/// registro la referencia calificada para no mezclarlas.
/// </summary>
public sealed class UbicacionInvalidaException : Exception
{
    public UbicacionInvalidaException(string message) : base(message)
    {
    }
}
