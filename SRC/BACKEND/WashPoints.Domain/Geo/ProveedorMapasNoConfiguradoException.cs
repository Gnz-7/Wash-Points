namespace WashPoints.Domain.Geo;

/// <summary>
/// El proveedor de mapas no está configurado: la geocodificación de texto no
/// puede resolverse. Es un fallo explícito, nunca un resultado vacío
/// (CU-01, CA-7.4).
/// </summary>
public sealed class ProveedorMapasNoConfiguradoException : Exception
{
    public ProveedorMapasNoConfiguradoException(string message) : base(message)
    {
    }
}
