namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Tipo de lavado: define la duración del turno y la seña a cobrar.
///
/// La duración fija es consecuencia de elegir la alternativa A (ARCHITECTURE
/// §3.1); está registrada como pregunta abierta en §10.5.
/// </summary>
public sealed class TipoLavado
{
    /// <summary>Constructor sin argumentos. Uso exclusivo de EF Core.</summary>
    public TipoLavado()
    {
    }

    public Guid Id { get; private set; }
    public Guid LavaderoId { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public int DuracionMinutos { get; private set; }
    public decimal PrecioTotal { get; private set; }
    public decimal PrecioSena { get; private set; }
    public bool Activo { get; private set; } = true;

    public static TipoLavado Crear(
        Guid lavaderoId,
        string nombre,
        int duracionMinutos,
        decimal precioTotal,
        decimal precioSena)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);

        if (duracionMinutos <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duracionMinutos), duracionMinutos, "La duración debe ser positiva.");
        }

        if (precioTotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precioTotal), precioTotal, "El precio total no puede ser negativo.");
        }

        if (precioSena < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precioSena), precioSena, "La seña no puede ser negativa.");
        }

        return new TipoLavado
        {
            Id = Guid.NewGuid(),
            LavaderoId = lavaderoId,
            Nombre = nombre.Trim(),
            DuracionMinutos = duracionMinutos,
            PrecioTotal = precioTotal,
            PrecioSena = precioSena,
        };
    }
}
