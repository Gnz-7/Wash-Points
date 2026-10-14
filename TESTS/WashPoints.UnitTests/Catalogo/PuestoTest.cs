using WashPoints.Domain.Catalogo;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// Ciclo de vida del puesto de lavado (SPECS/slice-04, CU-05).
///
/// `Activo` es el interruptor que habilita/deshabilita un puesto para reservas.
/// Las operaciones son idempotentes: activar un puesto activo, o desactivar uno
/// inactivo, no cambia el resultado ni lanza.
/// </summary>
public sealed class PuestoTest
{
    [Fact]
    public void NaceActivo()
    {
        var puesto = Puesto.Crear(Guid.NewGuid(), "Puesto 1");

        Assert.True(puesto.Activo);
    }

    [Fact]
    public void DesactivarLoDejaInactivo()
    {
        var puesto = Puesto.Crear(Guid.NewGuid(), "Puesto 1");

        puesto.Desactivar();

        Assert.False(puesto.Activo);
    }

    [Fact]
    public void ActivarLoReactiva()
    {
        var puesto = Puesto.Crear(Guid.NewGuid(), "Puesto 1");
        puesto.Desactivar();

        puesto.Activar();

        Assert.True(puesto.Activo);
    }

    [Fact]
    public void OperacionesSonIdempotentes()
    {
        var puesto = Puesto.Crear(Guid.NewGuid(), "Puesto 1");

        puesto.Desactivar();
        puesto.Desactivar();
        puesto.Activar();
        puesto.Activar();

        Assert.True(puesto.Activo);
    }
}
