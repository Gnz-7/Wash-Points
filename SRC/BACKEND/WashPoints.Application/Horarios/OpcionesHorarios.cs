using System.Globalization;
using WashPoints.Domain.Catalogo;

namespace WashPoints.Application.Horarios;

/// <summary>
/// Franjas por defecto que se siembran al dar de alta un lavadero
/// (SPECS/slice-17, D-17.2, R-1).
///
/// Es una clase de opciones plana, sin dependencias de Microsoft.Extensions,
/// porque la capa de Application sólo referencia Domain. La Api la construye
/// desde <c>Horarios:PorDefecto</c> y la registra como singleton; si la sección
/// no existe, caen estos valores y el alta nunca falla por configuración
/// ausente (mismo patrón que <c>OpcionesCatalogo</c>, D-13.7).
///
/// Un lavadero sin franjas está cerrado (D-17.2), así que sembrar el horario
/// en la misma transacción del alta es lo que evita que un lavadero nuevo nazca
/// inoperante.
/// </summary>
public sealed class OpcionesHorarios
{
    /// <summary>Nombre de la sección de configuración.</summary>
    public const string Seccion = "Horarios";

    /// <summary>
    /// Días que se siembran (ISO-8601). Vacío significa «usar los días por
    /// defecto» (lunes a sábado): el binder de configuración suma elementos a
    /// la colección existente, así que arrancar con valores precargados
    /// duplicaría las franjas al leer <c>Horarios:PorDefecto</c>.
    /// </summary>
    public IReadOnlyList<int> DiasPorDefecto { get; set; } = [];

    private static readonly int[] DiasPredeterminados = [1, 2, 3, 4, 5, 6];

    /// <summary>Hora de apertura por defecto, formato <c>HH:mm</c>.</summary>
    public string AperturaPorDefecto { get; set; } = "08:00";

    /// <summary>Hora de cierre por defecto, formato <c>HH:mm</c>.</summary>
    public string CierrePorDefecto { get; set; } = "20:00";

    /// <summary>
    /// Construye una franja activa por cada día configurado, con la apertura y
    /// el cierre por defecto.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si la configuración trae un día fuera de 1..7 o una hora con formato
    /// inválido. Falla en el alta, nunca de forma silenciosa.
    /// </exception>
    public IReadOnlyList<FranjaAtencion> CrearFranjasPorDefecto(
        Guid lavaderoId,
        DateTime creadoEnUtc)
    {
        var apertura = ParsearHora(AperturaPorDefecto, nameof(AperturaPorDefecto));
        var cierre = ParsearHora(CierrePorDefecto, nameof(CierrePorDefecto));

        if (apertura >= cierre)
        {
            throw new InvalidOperationException(
                "Horarios:PorDefecto tiene una apertura que no es anterior al cierre.");
        }

        var dias = DiasPorDefecto.Count == 0 ? DiasPredeterminados : DiasPorDefecto;
        var franjas = new List<FranjaAtencion>(dias.Count);

        foreach (var dia in dias.Distinct())
        {
            if (dia is < 1 or > 7)
            {
                throw new InvalidOperationException(
                    $"Horarios:PorDefecto:Dias incluye el día {dia}; se espera 1 a 7.");
            }

            franjas.Add(FranjaAtencion.Crear(lavaderoId, dia, apertura, cierre, creadoEnUtc));
        }

        return franjas;
    }

    private static TimeOnly ParsearHora(string valor, string clave)
    {
        if (!TimeOnly.TryParseExact(
                valor,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var hora))
        {
            throw new InvalidOperationException(
                $"Horarios:PorDefecto:{clave} debe tener formato HH:mm (valor actual: '{valor}').");
        }

        return hora;
    }
}
