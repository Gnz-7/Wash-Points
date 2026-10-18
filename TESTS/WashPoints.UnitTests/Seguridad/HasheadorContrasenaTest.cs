using WashPoints.Infrastructure.Seguridad;
using Xunit;

namespace WashPoints.UnitTests.Seguridad;

/// <summary>
/// Hashing de contraseñas con PBKDF2 (SPECS/slice-05 T-52, D-05.8). El claro
/// nunca se persiste ni aparece en el hash; el salt aleatorio hace que dos
/// hashes de la misma contraseña difieran.
/// </summary>
public sealed class HasheadorContrasenaTest
{
    private readonly HasheadorContrasenaPbkdf2 _hasheador = new();

    [Fact]
    public void HashearYVerificarAceptanLaContrasenaCorrecta()
    {
        var hash = _hasheador.Hashear("S3creta!2026");

        Assert.True(_hasheador.Verificar("S3creta!2026", hash));
    }

    [Fact]
    public void VerificarRechazaLaContrasenaIncorrecta()
    {
        var hash = _hasheador.Hashear("S3creta!2026");

        Assert.False(_hasheador.Verificar("otra-contrasena", hash));
    }

    [Fact]
    public void HashearNoEsDeterministaPorElSalAleatorio()
    {
        var primero = _hasheador.Hashear("misma-contrasena");
        var segundo = _hasheador.Hashear("misma-contrasena");

        Assert.NotEqual(primero, segundo);
    }

    [Fact]
    public void ElHashNoContieneLaContrasenaEnClaro()
    {
        var hash = _hasheador.Hashear("S3creta!2026");

        Assert.DoesNotContain("S3creta!2026", hash);
    }

    [Fact]
    public void VerificarRechazaUnHashMalformado()
    {
        Assert.False(_hasheador.Verificar("S3creta!2026", "no-es-un-hash-pbkdf2"));
    }

    [Fact]
    public void VerificarRechazaUnHashVacio()
    {
        Assert.False(_hasheador.Verificar("S3creta!2026", string.Empty));
    }

    [Fact]
    public void HashearRechazaUnaContrasenaVacia()
    {
        Assert.Throws<ArgumentException>(() => _hasheador.Hashear(string.Empty));
    }
}
