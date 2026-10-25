namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puerto de alta de clientes de negocio (slice-08, D-09.2).
///
/// Solo agrega: la lectura de clientes no la necesita ningún caso de uso
/// todavía, así que no se declara para no inventar contrato.
/// </summary>
public interface IClienteRepositorio
{
    /// <summary>
    /// Persiste el cliente dentro de la transacción abierta por
    /// <c>IUnidadDeTrabajo</c>; si la transacción revierte, no queda fila.
    /// </summary>
    Task AgregarAsync(Cliente cliente, CancellationToken ct);
}
