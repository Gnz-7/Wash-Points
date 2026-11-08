namespace WashPoints.Domain.Geo;

/// <summary>
/// El proveedor de mapas está configurado pero no respondió: red caída, timeout
/// o error del servidor. Se distingue de «no configurado» para que la UI pueda
/// ofrecer reintentar en vez de pedir configuración (D-16.4).
/// </summary>
public sealed class ProveedorMapasNoDisponibleException : Exception
{
    public ProveedorMapasNoDisponibleException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
