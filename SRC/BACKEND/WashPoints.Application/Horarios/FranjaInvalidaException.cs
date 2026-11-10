namespace WashPoints.Application.Horarios;

/// <summary>
/// Las franjas recibidas por el panel no son válidas (SPECS/slice-17 D-17.11).
///
/// Es un error de input, no de regla de reserva: el endpoint lo traduce a
/// <c>400 franja_invalida</c> y nunca a 422, que queda para las reglas
/// temporales (D-17.6).
/// </summary>
public sealed class FranjaInvalidaException : Exception
{
    public FranjaInvalidaException(string mensaje) : base(mensaje)
    {
    }
}
