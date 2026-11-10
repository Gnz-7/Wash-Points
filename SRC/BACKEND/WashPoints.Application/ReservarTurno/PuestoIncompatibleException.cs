namespace WashPoints.Application.ReservarTurno;

/// <summary>
/// El puesto no atiende el tipo de lavado elegido (SPECS/slice-17 D-17.7 y
/// D-17.10, R-4). La API lo traduce a <c>422 puesto_incompatible</c>.
/// </summary>
public sealed class PuestoIncompatibleException : Exception
{
    public PuestoIncompatibleException(string mensaje) : base(mensaje)
    {
    }
}
