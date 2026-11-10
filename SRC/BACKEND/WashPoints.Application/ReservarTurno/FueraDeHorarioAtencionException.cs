namespace WashPoints.Application.ReservarTurno;

/// <summary>
/// El rango del turno no entra completo en una franja de atención del lavadero
/// (SPECS/slice-17 D-17.4 y D-17.6, R-1). La API lo traduce a
/// <c>422 fuera_de_horario_atencion</c>.
/// </summary>
public sealed class FueraDeHorarioAtencionException : Exception
{
    public FueraDeHorarioAtencionException(string mensaje) : base(mensaje)
    {
    }
}
