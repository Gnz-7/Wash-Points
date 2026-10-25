namespace WashPoints.Application.Registro;

/// <summary>El nombre del lavadero llegó vacío o en blanco (slice-08, §5.4).</summary>
public sealed class LavaderoInvalidoException : Exception
{
    public LavaderoInvalidoException(string message) : base(message)
    {
    }
}
