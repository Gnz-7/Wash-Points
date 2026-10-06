using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Convierte una enumeración a su representación snake_case y vuelta.
///
/// La base guarda textos en snake_case (ver convención de columnas en
/// ARCHITECTURE §4.1 y la EXCLUDE en §4.2, que compara estados en minúscula:
/// `'confirmado'`, `'en_curso'`). EF mapearía por defecto a
/// <c>Enum.ToString()</c> ("Confirmado"), que no coincide con el predicado de
/// la restricción, y la EXCLUDE nunca se dispararía.
///
/// No hay mapeo por atributo porque la enumeración vive en el dominio y el
/// dominio no conoce la representación de la base (§10.6: la conversión de
/// representación es tema de infraestructura).
/// </summary>
public sealed class ConvertidorEnumSnakeCase<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    private static readonly Regex Separador =
        new("([a-z0-9])([A-Z])", RegexOptions.Compiled);

    public ConvertidorEnumSnakeCase()
        : base(
            value => ATexto(value),
            texto => (TEnum)Enum.Parse(typeof(TEnum), DeTexto(texto)))
    {
    }

    private static string ATexto(TEnum valor) =>
        Separador.Replace(valor.ToString()!, "$1_$2").ToLowerInvariant();

    private static string DeTexto(string texto) =>
        Regex.Replace(texto, "(^|_)([a-z])", m => m.Groups[2].Value.ToUpperInvariant(), RegexOptions.Compiled);
}