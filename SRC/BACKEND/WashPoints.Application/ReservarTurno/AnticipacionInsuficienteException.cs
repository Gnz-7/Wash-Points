namespace WashPoints.Application.ReservarTurno;

/// <summary>
/// El turno empieza en el pasado o dentro de la anticipación mínima
/// (SPECS/slice-17 D-17.6, R-2). La API lo traduce a
/// <c>422 anticipacion_insuficiente</c>.
/// </summary>
public sealed class AnticipacionInsuficienteException : Exception
{
    public AnticipacionInsuficienteException(string mensaje) : base(mensaje)
    {
    }
}
