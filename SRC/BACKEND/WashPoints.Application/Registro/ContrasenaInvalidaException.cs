namespace WashPoints.Application.Registro;

/// <summary>La contraseña no alcanza el mínimo exigido (slice-08, §5.4).</summary>
public sealed class ContrasenaInvalidaException : Exception
{
    public ContrasenaInvalidaException(string message) : base(message)
    {
    }
}
