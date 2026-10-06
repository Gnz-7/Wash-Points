namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Lavadero (comercio). Mínimo para resolver las FK de este slice:
/// ver DOCS/ARCHITECTURE.md §10.7 — el módulo Catalogo aún no está modelado.
/// </summary>
public sealed class Lavadero
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Lavadero()
    {
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public bool Activo { get; private set; } = true;

    public static Lavadero Crear(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        return new Lavadero { Id = Guid.NewGuid(), Nombre = nombre.Trim() };
    }
}
