namespace WashPoints.Application.ReservarTurno;

/// <summary>
/// El turno empieza más allá del horizonte máximo de reserva
/// (SPECS/slice-17 D-17.6, R-2). La API lo traduce a
/// <c>422 fuera_de_horizonte</c>.
/// </summary>
public sealed class FueraDeHorizonteException : Exception
{
    public FueraDeHorizonteException(string mensaje) : base(mensaje)
    {
    }
}
