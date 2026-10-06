using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Convierte <see cref="RangoTurno"/> (dominio, UTC) a la columna <c>tsrange</c>
/// (base, hora local sin zona) y viceversa.
///
/// Es el viaje de ida y vuelta de la única conversión de huso del sistema
/// (ARCHITECTURE §10.6): guardar y leer deben ser inversas, y la prueba
/// cruzada de T-09 lo verifica.
/// </summary>
public sealed class ConvertidorRangoTurno : ValueConverter<RangoTurno, NpgsqlRange<DateTime>>
{
    public static readonly ConvertidorRangoTurno Instancia = new();

    private ConvertidorRangoTurno()
        : base(
            rango => new NpgsqlRange<DateTime>(
                ConversorHorario.ALocal(rango.InicioUtc),
                true,
                ConversorHorario.ALocal(rango.FinUtc),
                false),
            rango => new RangoTurno(
                ConversorHorario.AUtc(rango.LowerBound),
                ConversorHorario.AUtc(rango.UpperBound)))
    {
    }
}