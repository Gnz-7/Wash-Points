namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Compatibilidad entre un puesto de lavado y un tipo de lavado (R-4,
/// SPECS/slice-17 D-17.7).
///
/// La ausencia de filas para un puesto significa «atiende todos los
/// servicios»: es la semántica retrocompatible que evita migrar los puestos
/// creados antes de este slice. Con filas, el puesto sólo atiende los tipos
/// listados.
/// </summary>
public sealed class PuestoTipoLavado
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public PuestoTipoLavado()
    {
    }

    public Guid PuestoId { get; private set; }

    public Guid TipoLavadoId { get; private set; }

    public static PuestoTipoLavado Crear(Guid puestoId, Guid tipoLavadoId)
    {
        if (puestoId == Guid.Empty)
        {
            throw new ArgumentException("La compatibilidad pertenece a un puesto.", nameof(puestoId));
        }

        if (tipoLavadoId == Guid.Empty)
        {
            throw new ArgumentException(
                "La compatibilidad referencia un tipo de lavado.",
                nameof(tipoLavadoId));
        }

        return new PuestoTipoLavado
        {
            PuestoId = puestoId,
            TipoLavadoId = tipoLavadoId,
        };
    }
}
