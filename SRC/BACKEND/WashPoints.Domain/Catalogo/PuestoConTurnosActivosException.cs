namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Se intentó desactivar un puesto que todavía tiene turnos vigentes
/// (<c>confirmado</c>/<c>en_curso</c>, o <c>pendiente_pago</c> con seña sin
/// vencer). Protege RB-2: no se libera capacidad con turnos que la ocupan. La
/// capa HTTP lo mapea a 409 <c>puesto_con_turnos_activos</c> (D5,
/// SPECS/slice-04, CA-S4.3).
/// </summary>
public sealed class PuestoConTurnosActivosException : Exception
{
    public PuestoConTurnosActivosException(string mensaje) : base(mensaje)
    {
    }
}
