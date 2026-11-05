namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puerto de persistencia de tipos de lavado (SPECS/slice-13 T-131, D-13.1).
///
/// El catálogo es por lavadero: toda lectura y escritura se acota por
/// <c>lavaderoId</c>, y eso es lo que convierte un tipo de otro lavadero en
/// inexistente para el llamador (D-13.9). Igual que
/// <see cref="IPuestoRepositorio"/>, no dispara su propio `SaveChangesAsync`.
/// </summary>
public interface ITipoLavadoRepositorio
{
    Task AgregarAsync(TipoLavado tipo, CancellationToken ct);

    Task<TipoLavado?> ObtenerAsync(Guid lavaderoId, Guid tipoId, CancellationToken ct);

    Task<IReadOnlyList<TipoLavado>> ListarAsync(Guid lavaderoId, CancellationToken ct);

    Task GuardarAsync(CancellationToken ct);
}
