using WashPoints.Domain.Catalogo;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// Ciclo de vida del tipo de lavado (SPECS/slice-13, T-130).
///
/// Un tipo de lavado define duración y precios, y se administra con baja
/// lógica: se activa y se desactiva, nunca se borra (D-13.3). Las operaciones
/// de estado son idempotentes, igual que en `PuestoTest`.
///
/// La seña es un importe propio y nunca puede ser cero: una seña en cero
/// anularía la regla RB-1 (la reserva dejaría de exigir pago) — D-13.2.
/// </summary>
public sealed class TipoLavadoTest
{
    private static TipoLavado CrearTipo() =>
        TipoLavado.Crear(Guid.NewGuid(), "Lavado básico", 30, 8000m, 1500m);

    [Fact]
    public void NaceActivo()
    {
        var tipo = CrearTipo();

        Assert.True(tipo.Activo);
    }

    [Fact]
    public void DesactivarLoDejaInactivo()
    {
        var tipo = CrearTipo();

        tipo.Desactivar();

        Assert.False(tipo.Activo);
    }

    [Fact]
    public void ActivarLoReactiva()
    {
        var tipo = CrearTipo();
        tipo.Desactivar();

        tipo.Activar();

        Assert.True(tipo.Activo);
    }

    [Fact]
    public void OperacionesDeEstadoSonIdempotentes()
    {
        var tipo = CrearTipo();

        tipo.Desactivar();
        tipo.Desactivar();
        tipo.Activar();
        tipo.Activar();

        Assert.True(tipo.Activo);
    }

    [Fact]
    public void EditarCambiaNombreDuracionYPrecios()
    {
        var tipo = CrearTipo();

        tipo.Editar("Lavado completo", 60, 15000m, 2500m);

        Assert.Equal("Lavado completo", tipo.Nombre);
        Assert.Equal(60, tipo.DuracionMinutos);
        Assert.Equal(15000m, tipo.PrecioTotal);
        Assert.Equal(2500m, tipo.PrecioSena);
    }

    [Fact]
    public void EditarRecortaEspaciosDelNombre()
    {
        var tipo = CrearTipo();

        tipo.Editar("  Lavado completo  ", 60, 15000m, 2500m);

        Assert.Equal("Lavado completo", tipo.Nombre);
    }

    [Fact]
    public void EditarNoTocaElEstado()
    {
        var tipo = CrearTipo();
        tipo.Desactivar();

        tipo.Editar("Lavado completo", 60, 15000m, 2500m);

        Assert.False(tipo.Activo);
    }

    [Fact]
    public void CrearRechazaSenaEnCero()
    {
        var excepcion = Assert.Throws<ArgumentOutOfRangeException>(
            () => TipoLavado.Crear(Guid.NewGuid(), "Lavado básico", 30, 8000m, 0m));

        Assert.Equal("precioSena", excepcion.ParamName);
    }

    [Fact]
    public void CrearRechazaSenaMayorAlPrecioTotal()
    {
        var excepcion = Assert.Throws<ArgumentOutOfRangeException>(
            () => TipoLavado.Crear(Guid.NewGuid(), "Lavado básico", 30, 8000m, 9000m));

        Assert.Equal("precioSena", excepcion.ParamName);
    }

    [Fact]
    public void CrearRechazaDuracionNoPositiva()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TipoLavado.Crear(Guid.NewGuid(), "Lavado básico", 0, 8000m, 1500m));
    }

    [Fact]
    public void CrearRechazaNombreVacio()
    {
        Assert.Throws<ArgumentException>(
            () => TipoLavado.Crear(Guid.NewGuid(), "   ", 30, 8000m, 1500m));
    }

    [Fact]
    public void EditarRechazaNombreVacio()
    {
        var tipo = CrearTipo();

        Assert.Throws<ArgumentException>(
            () => tipo.Editar("   ", 30, 8000m, 1500m));
    }

    [Fact]
    public void EditarRechazaDuracionNoPositiva()
    {
        var tipo = CrearTipo();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => tipo.Editar("Lavado básico", -5, 8000m, 1500m));
    }

    [Fact]
    public void EditarRechazaSenaEnCero()
    {
        var tipo = CrearTipo();

        var excepcion = Assert.Throws<ArgumentOutOfRangeException>(
            () => tipo.Editar("Lavado básico", 30, 8000m, 0m));

        Assert.Equal("precioSena", excepcion.ParamName);
    }

    [Fact]
    public void EditarRechazaSenaMayorAlPrecioTotal()
    {
        var tipo = CrearTipo();

        var excepcion = Assert.Throws<ArgumentOutOfRangeException>(
            () => tipo.Editar("Lavado básico", 30, 8000m, 9000m));

        Assert.Equal("precioSena", excepcion.ParamName);
    }

    [Fact]
    public void UnEditarRechazadoNoDejaCambiosParciales()
    {
        var tipo = CrearTipo();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => tipo.Editar("Otro nombre", 60, 15000m, 0m));

        Assert.Equal("Lavado básico", tipo.Nombre);
        Assert.Equal(30, tipo.DuracionMinutos);
        Assert.Equal(8000m, tipo.PrecioTotal);
        Assert.Equal(1500m, tipo.PrecioSena);
    }
}
