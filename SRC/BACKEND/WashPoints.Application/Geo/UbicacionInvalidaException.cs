namespace WashPoints.Application.Geo;

/// <summary>Las coordenadas recibidas no están dentro de los rangos válidos.</summary>
public sealed class UbicacionInvalidaException : Exception
{
    public UbicacionInvalidaException(string message) : base(message)
    {
    }
}
