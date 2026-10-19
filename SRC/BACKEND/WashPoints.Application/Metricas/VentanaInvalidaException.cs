namespace WashPoints.Application.Metricas;

/// <summary>
/// La ventana de consulta es inválida: el fin no es posterior al inicio.
/// La API la traduce a <c>400 ventana_invalida</c>.
/// </summary>
public sealed class VentanaInvalidaException : Exception
{
    public VentanaInvalidaException()
        : base("La ventana de consulta debe tener un inicio anterior al fin.")
    {
    }
}
