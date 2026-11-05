namespace WashPoints.Domain.Catalogo;

/// <summary>
/// Tipo de lavado: define la duración del turno y la seña a cobrar.
///
/// La duración fija es consecuencia de elegir la alternativa A (ARCHITECTURE
/// §3.1); está registrada como pregunta abierta en §10.5.
///
/// Un tipo de lavado nunca se borra: se desactiva (D-13.3). La baja es lógica
/// para no romper la trazabilidad de los turnos históricos que lo referencian.
///
/// La seña es un importe propio y siempre mayor a cero (D-13.2). Una seña en
/// cero anularía de hecho la regla RB-1, porque la reserva dejaría de exigir
/// pago previo.
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
        Validar(nombre, duracionMinutos, precioTotal, precioSena);

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

    /// <summary>
    /// Actualiza los datos editables del catálogo. No cambia el estado: un tipo
    /// desactivado sigue desactivado después de editarse.
    ///
    /// Valida todo antes de asignar, para que un rechazo no deje el objeto a
    /// medio modificar.
    ///
    /// El cambio de duración aplica sólo a turnos nuevos: los turnos ya creados
    /// conservan inmutable su rango [desde, hasta] pactado (D-13.6).
    /// </summary>
    public void Editar(
        string nombre,
        int duracionMinutos,
        decimal precioTotal,
        decimal precioSena)
    {
        Validar(nombre, duracionMinutos, precioTotal, precioSena);

        Nombre = nombre.Trim();
        DuracionMinutos = duracionMinutos;
        PrecioTotal = precioTotal;
        PrecioSena = precioSena;
    }

    /// <summary>Habilita el tipo para reservas nuevas. Idempotente.</summary>
    public void Activar() => Activo = true;

    /// <summary>
    /// Deshabilita el tipo para reservas nuevas. Idempotente.
    ///
    /// Que la baja sea idempotente no exime al servicio de comprobar turnos
    /// vigentes antes de desactivar: esa regla vive en la capa de aplicación
    /// (D-13.4), porque el dominio no consulta la base.
    /// </summary>
    public void Desactivar() => Activo = false;

    private static void Validar(
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

        if (precioSena <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precioSena), precioSena, "La seña debe ser mayor a cero.");
        }

        if (precioSena > precioTotal)
        {
            throw new ArgumentOutOfRangeException(
                nameof(precioSena), precioSena, "La seña no puede superar el precio total.");
        }
    }
}
