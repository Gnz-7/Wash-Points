namespace WashPoints.Application.Geo;

/// <summary>El radio de búsqueda está fuera del rango admitido.</summary>
public sealed class RadioInvalidoException : Exception
{
    public RadioInvalidoException(string message) : base(message)
    {
    }
}
