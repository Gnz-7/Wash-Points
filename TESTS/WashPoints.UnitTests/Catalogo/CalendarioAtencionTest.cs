using WashPoints.Domain.Catalogo;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// Regla de horario de atención (SPECS/slice-17, D-17.4, R-1).
///
/// El calendario es puro: recibe las franjas del lavadero y un rango local y
/// decide si el turno entra completo en una franja activa del mismo día. Un
/// rango que empieza antes de abrir, termina después de cerrar, cruza el corte
/// del mediodía o cae en un día sin franjas se rechaza.
/// </summary>
public sealed class CalendarioAtencionTest
{
    private static readonly Guid LavaderoId = Guid.NewGuid();
    private static readonly DateTime CreadoEn = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static FranjaAtencion Franja(int dia, string apertura, string cierre, bool activo = true)
    {
        var franja = FranjaAtencion.Crear(
            LavaderoId,
            dia,
            TimeOnly.Parse(apertura),
            TimeOnly.Parse(cierre),
            CreadoEn);

        if (!activo)
        {
            franja.Desactivar();
        }

        return franja;
    }

    private static readonly IReadOnlyList<FranjaAtencion> SemanaConCorte =
    [
        Franja(1, "08:00", "13:00"),
        Franja(1, "14:00", "20:00"),
    ];

    [Fact]
    public void AceptaUnTurnoQueEntraCompletoEnLaFranja()
    {
        var inicio = new DateTime(2026, 11, 9, 10, 0, 0);
        var fin = inicio.AddMinutes(30);

        Assert.True(CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, fin));
    }

    [Fact]
    public void AceptaElBordeExactoDeAperturaYCierre()
    {
        var inicio = new DateTime(2026, 11, 9, 8, 0, 0);
        var fin = new DateTime(2026, 11, 9, 13, 0, 0);

        Assert.True(CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, fin));
    }

    [Fact]
    public void RechazaUnInicioAnteriorALaApertura()
    {
        var inicio = new DateTime(2026, 11, 9, 7, 45, 0);

        Assert.False(
            CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, inicio.AddMinutes(30)));
    }

    [Fact]
    public void RechazaUnFinPosteriorAlCierre()
    {
        var inicio = new DateTime(2026, 11, 9, 12, 45, 0);

        Assert.False(
            CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, inicio.AddMinutes(30)));
    }

    [Fact]
    public void RechazaUnTurnoQueCruzaElCorteDelMediodia()
    {
        var inicio = new DateTime(2026, 11, 9, 12, 45, 0);

        Assert.False(
            CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, inicio.AddMinutes(30)));
    }

    [Fact]
    public void RechazaUnTurnoQueTerminaAlDiaSiguiente()
    {
        var inicio = new DateTime(2026, 11, 9, 23, 45, 0);
        var fin = inicio.AddMinutes(30);

        Assert.False(CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, fin));
    }

    [Fact]
    public void RechazaUnDiaSinFranjas()
    {
        // Domingo: la semana por defecto no lo incluye.
        var inicio = new DateTime(2026, 11, 8, 10, 0, 0);

        Assert.False(
            CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, inicio.AddMinutes(30)));
    }

    [Fact]
    public void IgnoraLasFranjasInactivas()
    {
        IReadOnlyList<FranjaAtencion> franjas = [Franja(1, "08:00", "20:00", activo: false)];
        var inicio = new DateTime(2026, 11, 9, 10, 0, 0);

        Assert.False(CalendarioAtencion.EstaAbierto(franjas, inicio, inicio.AddMinutes(30)));
    }

    [Fact]
    public void RechazaUnRangoVacioOInvertido()
    {
        var inicio = new DateTime(2026, 11, 9, 10, 0, 0);

        Assert.False(CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, inicio));
        Assert.False(
            CalendarioAtencion.EstaAbierto(SemanaConCorte, inicio, inicio.AddMinutes(-30)));
    }

    [Fact]
    public void DevuelveLasFranjasDelDiaIndicado()
    {
        var delLunes = CalendarioAtencion.FranjasDelDia(SemanaConCorte, new DateTime(2026, 11, 9));

        Assert.Equal(2, delLunes.Count);
        Assert.Empty(CalendarioAtencion.FranjasDelDia(SemanaConCorte, new DateTime(2026, 11, 8)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void NoSePuedeCrearUnaFranjaConUnDiaFueraDeRango(int dia)
    {
        Assert.Throws<ArgumentException>(() =>
            FranjaAtencion.Crear(LavaderoId, dia, new TimeOnly(8, 0), new TimeOnly(20, 0), CreadoEn));
    }

    [Fact]
    public void NoSePuedeCrearUnaFranjaQueCierraAntesDeAbrir()
    {
        Assert.Throws<ArgumentException>(() =>
            FranjaAtencion.Crear(
                LavaderoId,
                1,
                new TimeOnly(20, 0),
                new TimeOnly(8, 0),
                CreadoEn));
    }

    [Fact]
    public void UnaFranjaNuevaNaceActiva()
    {
        Assert.True(Franja(1, "08:00", "20:00").Activo);
    }
}
