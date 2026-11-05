namespace WashPoints.Application.Catalogo;

/// <summary>
/// Precios por defecto del catálogo que se siembra al dar de alta un lavadero
/// (SPECS/slice-13 §5.5, D-13.7 y D-13.8).
///
/// Es una clase de opciones plana, sin dependencias de Microsoft.Extensions,
/// porque la capa de Application sólo referencia Domain. La Api la construye
/// desde <c>Catalogo:PreciosPorDefecto</c> y la registra como singleton; si la
/// sección no existe, caen estos valores y el alta nunca falla por
/// configuración ausente.
/// </summary>
public sealed class OpcionesCatalogo
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Catalogo";

    /// <summary>Precios del tipo «Lavado básico» (30 minutos).</summary>
    public PrecioTipoLavado LavadoBasico { get; set; } = new()
    {
        PrecioTotal = 8000m,
        PrecioSena = 1500m,
    };

    /// <summary>Precios del tipo «Lavado completo» (60 minutos).</summary>
    public PrecioTipoLavado LavadoCompleto { get; set; } = new()
    {
        PrecioTotal = 15000m,
        PrecioSena = 2500m,
    };
}

/// <summary>Precio total y seña de un tipo de lavado por defecto.</summary>
public sealed class PrecioTipoLavado
{
    public decimal PrecioTotal { get; set; }

    public decimal PrecioSena { get; set; }
}
