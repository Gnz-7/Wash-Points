namespace WashPoints.Application.Geo;

/// <summary>El proveedor de mapas no reconoce la dirección escrita.</summary>
public sealed class DireccionNoEncontradaException : Exception
{
    public DireccionNoEncontradaException(string message) : base(message)
    {
    }
}
