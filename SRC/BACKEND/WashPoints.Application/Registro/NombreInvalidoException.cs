namespace WashPoints.Application.Registro;

/// <summary>El nombre de la persona llegó vacío o en blanco (slice-08, §5.4).</summary>
public sealed class NombreInvalidoException : Exception
{
    public NombreInvalidoException(string message) : base(message)
    {
    }
}
