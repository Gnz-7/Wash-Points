namespace WashPoints.Domain.Catalogo;

/// <summary>
/// El tipo de lavado tiene turnos vigentes y no se puede desactivar
/// (SPECS/slice-13 D-13.4). La API responde 409 <c>tipo_con_turnos_activos</c>.
///
/// Vigente es el predicado formal: estado no terminal (<c>pendiente_pago</c>,
/// <c>confirmado</c> o <c>en_curso</c>) y <c>hasta</c> mayor o igual al momento
/// actual. Un turno terminal no bloquea aunque su ventana sea futura.
/// </summary>
public sealed class TipoLavadoConTurnosActivosException : Exception
{
    public TipoLavadoConTurnosActivosException(string message)
        : base(message)
    {
    }
}
