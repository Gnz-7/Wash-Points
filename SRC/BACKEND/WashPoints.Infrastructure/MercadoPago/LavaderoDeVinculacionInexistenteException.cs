namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// El lavadero pedido para la vinculación OAuth no existe (slice-20, §4.1).
/// El endpoint <c>conectar</c> lo traduce a 404 <c>lavadero_inexistente</c>.
/// </summary>
public sealed class LavaderoDeVinculacionInexistenteException : Exception
{
    public LavaderoDeVinculacionInexistenteException(Guid lavaderoId)
        : base($"El lavadero {lavaderoId} no existe.")
    {
    }
}
