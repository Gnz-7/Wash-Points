namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Cliente (conductor). Mínimo para resolver el atributo `cliente_id` de
/// `turnos`; ver DOCS/ARCHITECTURE.md §10.7.
///
/// Deliberadamente sin teléfono, patente ni contacto: son datos personales y
/// AGENTS.md §3.4 exige minimizarlos mientras el módulo no los necesite.
/// </summary>
public sealed class Cliente
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Cliente()
    {
    }

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = string.Empty;

    public static Cliente Crear(string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        return new Cliente { Id = Guid.NewGuid(), Nombre = nombre.Trim() };
    }
}
