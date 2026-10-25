namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puerto de alta de lavaderos (slice-08, D-09.3).
///
/// El alta de un administrador crea su lavadero en la misma transacción: si
/// la credencial no se puede crear, el lavadero no debe quedar huérfano.
/// </summary>
public interface ILavaderoRepositorio
{
    /// <summary>
    /// Persiste el lavadero dentro de la transacción abierta por
    /// <c>IUnidadDeTrabajo</c>; si la transacción revierte, no queda fila.
    /// </summary>
    Task AgregarAsync(Lavadero lavadero, CancellationToken ct);
}
