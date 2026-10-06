namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Puesto de lavado. Es la unidad sobre la que recae la exclusión de
/// solapamiento (ARCHITECTURE §4.1): la restricción agrupa por
/// <see cref="Id"/> y solapa el rango del turno.
///
/// Mínimo para resolver la FK de `turnos.puesto_id`; ver §10.7.
/// </summary>
public sealed class Puesto
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public Puesto()
    {
    }

    public Guid Id { get; private set; }
    public Guid LavaderoId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public bool Activo { get; private set; } = true;

    public static Puesto Crear(Guid lavaderoId, string nombre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        if (lavaderoId == Guid.Empty)
        {
            throw new ArgumentException("El puesto pertenece a un lavadero.", nameof(lavaderoId));
        }

        return new Puesto
        {
            Id = Guid.NewGuid(),
            LavaderoId = lavaderoId,
            Nombre = nombre.Trim(),
        };
    }
}
