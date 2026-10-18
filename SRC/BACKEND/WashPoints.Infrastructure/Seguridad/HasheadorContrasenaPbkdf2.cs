using System.Security.Cryptography;
using WashPoints.Domain.Seguridad;

namespace WashPoints.Infrastructure.Seguridad;

/// <summary>
/// Hashing de contraseñas con PBKDF2-HMAC-SHA256 (D-05.8, sin dependencias de
/// runtime nuevas). El hash almacenado tiene el formato
/// <c>pbkdf2$&lt;iteraciones&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;</c> y la
/// verificación es de tiempo constante.
/// </summary>
public sealed class HasheadorContrasenaPbkdf2 : IHasheadorContrasena
{
    private const string Prefijo = "pbkdf2";
    private const int Iteraciones = 100_000;
    private const int LargoSal = 16;
    private const int LargoHash = 32;

    /// <inheritdoc />
    public string Hashear(string contrasena)
    {
        ArgumentException.ThrowIfNullOrEmpty(contrasena);

        var sal = RandomNumberGenerator.GetBytes(LargoSal);
        var subclave = Rfc2898DeriveBytes.Pbkdf2(
            contrasena, sal, Iteraciones, HashAlgorithmName.SHA256, LargoHash);

        return string.Join(
            '$',
            Prefijo,
            Iteraciones.ToString(),
            Convert.ToBase64String(sal),
            Convert.ToBase64String(subclave));
    }

    /// <inheritdoc />
    public bool Verificar(string contrasena, string hash)
    {
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var partes = hash.Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones) || iteraciones <= 0)
        {
            return false;
        }

        byte[] sal;
        byte[] esperado;
        try
        {
            sal = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (sal.Length == 0 || esperado.Length == 0)
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(
            contrasena, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);

        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}
