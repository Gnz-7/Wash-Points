using WashPoints.Domain.Catalogo;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// T-218: vigencia de la suscripción mensual mock (D-20.7, CA-S20.8/S20.9).
///
/// Una suscripción activa está vigente si no tiene vencimiento registrado o si
/// el vencimiento es futuro. El panel muestra «Suscripción activa» sólo en ese
/// caso; una pendiente (activa=false) o vencida no lo está.
/// </summary>
public sealed class LavaderoSuscripcionVigenteTest
{
    private static readonly DateTimeOffset Ahora =
        new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnaSuscripcionPendienteNoEstaVigente()
    {
        var lavadero = Lavadero.Crear("Lavadero Central");

        Assert.False(lavadero.SuscripcionVigente(Ahora));
    }

    [Fact]
    public void UnaSuscripcionActivaConVencimientoFuturoEstaVigente()
    {
        var lavadero = Lavadero.Crear("Lavadero Central");
        lavadero.ActivarSuscripcion(Ahora.AddDays(30));

        Assert.True(lavadero.SuscripcionVigente(Ahora));
    }

    [Fact]
    public void UnaSuscripcionActivaSinVencimientoRegistradoEstaVigente()
    {
        var lavadero = Lavadero.Crear("Lavadero Central");
        lavadero.ActivarSuscripcion(Ahora.AddDays(30));
        // El backfill del slice-20 deja activas sin vencimiento registrado.
        lavadero.GetType().GetProperty("SuscripcionVenceEl")!.SetValue(lavadero, null);

        Assert.True(lavadero.SuscripcionVigente(Ahora));
    }

    [Fact]
    public void UnaSuscripcionVencidaNoEstaVigente()
    {
        var lavadero = Lavadero.Crear("Lavadero Central");
        lavadero.ActivarSuscripcion(Ahora.AddDays(-1));

        Assert.False(lavadero.SuscripcionVigente(Ahora));
    }

    [Fact]
    public void ElVencimientoEsInclusivoEnElBordeExacto()
    {
        var lavadero = Lavadero.Crear("Lavadero Central");
        lavadero.ActivarSuscripcion(Ahora);

        Assert.True(lavadero.SuscripcionVigente(Ahora));
    }
}
