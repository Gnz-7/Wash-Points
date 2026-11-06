using WashPoints.Domain.Reembolsos;
using Xunit;

namespace WashPoints.UnitTests.Reembolsos;

/// <summary>
/// Protege la política de reembolso del slice-14 (D-14.6, D-14.7).
///
/// La política es la única que decide si la seña se devuelve, cuánto y por qué.
/// Si alguien cambia el plazo o el porcentaje en el código en vez de hacerlo por
/// configuración, estos tests lo delatan con los valores por defecto (120
/// minutos y 100 %).
/// </summary>
public sealed class PoliticaReembolsoTest
{
    private static readonly DateTime Inicio = new(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc);

    private static PoliticaReembolso ConOpciones(
        int anticipacionMinimaMinutos = 120,
        int porcentajeReintegrable = 100) =>
        new(new OpcionesReembolso
        {
            AnticipacionMinimaMinutos = anticipacionMinimaMinutos,
            PorcentajeReintegrable = porcentajeReintegrable,
        });

    private static DateTime ConAnticipacion(int minutos) => Inicio.AddMinutes(-minutos);

    [Fact]
    public void EnPlazoLaSenaSeReintegraCompleta()
    {
        var evaluacion = ConOpciones().Evaluar(
            precioSena: 1500m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(180));

        Assert.Equal(EstadoReembolso.Pendiente, evaluacion.Estado);
        Assert.Equal(MotivoReembolso.ClienteEnPlazo, evaluacion.Motivo);
        Assert.Equal(1500m, evaluacion.Monto);
    }

    [Fact]
    public void ElLimiteDelPlazoSeIncluye()
    {
        var evaluacion = ConOpciones().Evaluar(
            precioSena: 1500m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(120));

        Assert.Equal(EstadoReembolso.Pendiente, evaluacion.Estado);
        Assert.Equal(1500m, evaluacion.Monto);
    }

    [Fact]
    public void UnMinutoAntesDelLimiteYaNoHayReintegro()
    {
        var evaluacion = ConOpciones().Evaluar(
            precioSena: 1500m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(119));

        Assert.Equal(EstadoReembolso.NoAplica, evaluacion.Estado);
        Assert.Equal(MotivoReembolso.ClienteFueraDePlazo, evaluacion.Motivo);
        Assert.Equal(0m, evaluacion.Monto);
    }

    [Fact]
    public void CancelarDespuesDelInicioTampocoReintegra()
    {
        var evaluacion = ConOpciones().Evaluar(
            precioSena: 1500m,
            inicioUtc: Inicio,
            ahoraUtc: Inicio.AddMinutes(5));

        Assert.Equal(EstadoReembolso.NoAplica, evaluacion.Estado);
        Assert.Equal(MotivoReembolso.ClienteFueraDePlazo, evaluacion.Motivo);
        Assert.Equal(0m, evaluacion.Monto);
    }

    [Fact]
    public void UnTurnoSinPagoNoGeneraReintegro()
    {
        var evaluacion = ConOpciones().Evaluar(
            precioSena: 0m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(600));

        Assert.Equal(EstadoReembolso.NoAplica, evaluacion.Estado);
        Assert.Equal(MotivoReembolso.SinPago, evaluacion.Motivo);
        Assert.Equal(0m, evaluacion.Monto);
    }

    [Theory]
    [InlineData(50, 500)]
    [InlineData(100, 1000)]
    [InlineData(33, 330)]
    public void ElPorcentajeReintegrableSeAplicaSobreLaSena(int porcentaje, int esperado)
    {
        var evaluacion = ConOpciones(porcentajeReintegrable: porcentaje).Evaluar(
            precioSena: 1000m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(180));

        Assert.Equal(EstadoReembolso.Pendiente, evaluacion.Estado);
        Assert.Equal(esperado, evaluacion.Monto);
    }

    [Fact]
    public void UnPorcentajeCeroNoGeneraReintegro()
    {
        var evaluacion = ConOpciones(porcentajeReintegrable: 0).Evaluar(
            precioSena: 1000m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(180));

        Assert.Equal(EstadoReembolso.NoAplica, evaluacion.Estado);
        Assert.Equal(MotivoReembolso.ClienteEnPlazo, evaluacion.Motivo);
        Assert.Equal(0m, evaluacion.Monto);
    }

    [Fact]
    public void ElMontoSeRedondeaADosDecimales()
    {
        var evaluacion = ConOpciones(porcentajeReintegrable: 33).Evaluar(
            precioSena: 1000.55m,
            inicioUtc: Inicio,
            ahoraUtc: ConAnticipacion(180));

        Assert.Equal(330.18m, evaluacion.Monto);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void UnPorcentajeFueraDeRangoEsInvalido(int porcentaje)
    {
        var opciones = new OpcionesReembolso { PorcentajeReintegrable = porcentaje };

        Assert.Throws<ArgumentOutOfRangeException>(opciones.Validar);
    }

    [Fact]
    public void UnaAnticipacionNegativaEsInvalida()
    {
        var opciones = new OpcionesReembolso { AnticipacionMinimaMinutos = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(opciones.Validar);
    }

    [Fact]
    public void LosValoresPorDefectoSonLosAcordados()
    {
        var opciones = new OpcionesReembolso();

        Assert.Equal(120, opciones.AnticipacionMinimaMinutos);
        Assert.Equal(100, opciones.PorcentajeReintegrable);
        opciones.Validar();
    }
}
