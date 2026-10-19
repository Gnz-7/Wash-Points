using WashPoints.Application.Metricas;
using WashPoints.Domain.Metricas;
using Xunit;

namespace WashPoints.UnitTests.Metricas;

/// <summary>
/// T-62: composición del resumen y cálculo de la tasa de ocupación (CU-09).
///
/// CA-S6.2 — señas, facturado total y facturado de mostrador no se suman.
/// CA-S6.4 — un día sin actividad produce ceros, nunca un error.
/// CA-S6.6 — la tasa se calcula sobre la ventana declarada.
///
/// El puerto <see cref="IMetricasConsulta"/> se reemplaza por un fake: acá se
/// prueba la aritmética y la forma del contrato, no el SQL (eso es T-63).
///
/// Ver SPECS/slice-06-metricas-bi.md T-62 y §5.
/// </summary>
public sealed class MetricasServicioTest
{
    private static readonly Guid LavaderoId = Guid.NewGuid();
    private static readonly Guid PuestoId = Guid.NewGuid();

    private static readonly DateOnly Dia = new(2026, 10, 10);

    /// <summary>Día local completo del 2026-10-10 en UTC (UTC-3, sin DST).</summary>
    private static readonly DateTime DesdeUtc = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime HastaUtc = new(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CalculadoEnUtc = new(2026, 10, 10, 14, 3, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UnDiaSinActividadDevuelveCeros()
    {
        var servicio = new MetricasServicio(new ConsultaMetricasFake());

        var resumen = await servicio.ObtenerResumenAsync(LavaderoId, Dia, DesdeUtc, HastaUtc);

        Assert.Equal(Dia, resumen.Fecha);
        Assert.Equal(0m, resumen.Ingresos.SenasCobradas);
        Assert.Equal(0m, resumen.Ingresos.FacturadoTotal);
        Assert.Equal(0m, resumen.Ingresos.FacturadoMostrador);
        Assert.Equal(0, resumen.Turnos.Atendidos);
        Assert.Equal(0, resumen.Turnos.Espontaneos);
        Assert.Equal(0, resumen.Turnos.NoPresentados);
        Assert.Equal(0, resumen.Turnos.Cancelaciones);
        Assert.Equal(0, resumen.Capacidad.PuestosTotal);
        Assert.Equal(0, resumen.Capacidad.PuestosActivos);
        Assert.Equal(0, resumen.Capacidad.PuestosOcupados);
    }

    [Fact]
    public async Task LasTresMagnitudesNoSeSuman()
    {
        var consulta = new ConsultaMetricasFake
        {
            Resumen = new ResumenMetricas(
                PuestosTotal: 4,
                PuestosActivos: 3,
                PuestosOcupados: 2,
                CalculadoEnUtc: CalculadoEnUtc,
                SenasCobradas: 9000m,
                FacturadoTotal: 45000m,
                FacturadoMostrador: 15000m,
                Atendidos: 6,
                Espontaneos: 2,
                NoPresentados: 1,
                Cancelaciones: 3),
        };

        var servicio = new MetricasServicio(consulta);
        var resumen = await servicio.ObtenerResumenAsync(LavaderoId, Dia, DesdeUtc, HastaUtc);

        // Cada magnitud viaja con su valor: la seña es un adelanto y el
        // facturado es el servicio entregado (CA-8.2).
        Assert.Equal(9000m, resumen.Ingresos.SenasCobradas);
        Assert.Equal(45000m, resumen.Ingresos.FacturadoTotal);
        Assert.Equal(15000m, resumen.Ingresos.FacturadoMostrador);
        Assert.Equal("ARS", resumen.Ingresos.Moneda);

        // Y no existe una cuarta magnitud de dinero que las agregue
        // (54000 sería el "ingreso total" que el SDD no define).
        var magnitudes = typeof(IngresosDelDia)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(decimal))
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(3, magnitudes.Length);
    }

    [Fact]
    public async Task LaTasaSeRecortaALaVentana()
    {
        var consulta = new ConsultaMetricasFake
        {
            Ocupacion =
            [
                new OcupacionPuesto(PuestoId, "Puesto 1", MinutosOcupados: 20),
            ],
        };

        var servicio = new MetricasServicio(consulta);
        var desde = DesdeUtc;
        var hasta = DesdeUtc.AddHours(1);

        var ocupacion = await servicio.ObtenerOcupacionAsync(LavaderoId, desde, hasta);

        var puesto = Assert.Single(ocupacion.Puestos);
        Assert.Equal(PuestoId, puesto.PuestoId);
        Assert.Equal(20, puesto.MinutosOcupados);
        // La ventana no es el día entero: se declara y se usa como denominador.
        Assert.Equal(60, puesto.MinutosVentana);
        Assert.Equal(0.3333m, puesto.Tasa);
    }

    [Fact]
    public async Task UnaVentanaInvalidaSeRechaza()
    {
        var servicio = new MetricasServicio(new ConsultaMetricasFake());

        await Assert.ThrowsAsync<VentanaInvalidaException>(
            () => servicio.ObtenerOcupacionAsync(LavaderoId, DesdeUtc, DesdeUtc));
    }

    /// <summary>
    /// Puerto de lectura falso: devuelve lo que cada test configure. Por
    /// defecto, un día sin actividad (ceros).
    /// </summary>
    private sealed class ConsultaMetricasFake : IMetricasConsulta
    {
        public ResumenMetricas Resumen { get; set; } = new(
            PuestosTotal: 0,
            PuestosActivos: 0,
            PuestosOcupados: 0,
            CalculadoEnUtc: CalculadoEnUtc,
            SenasCobradas: 0m,
            FacturadoTotal: 0m,
            FacturadoMostrador: 0m,
            Atendidos: 0,
            Espontaneos: 0,
            NoPresentados: 0,
            Cancelaciones: 0);

        public IReadOnlyList<OcupacionPuesto> Ocupacion { get; set; } = [];

        public Task<ResumenMetricas> ObtenerResumenAsync(
            Guid lavaderoId,
            DateTime desdeUtc,
            DateTime hastaUtc,
            CancellationToken ct = default)
        {
            Assert.Equal(LavaderoId, lavaderoId);
            Assert.Equal(DesdeUtc, desdeUtc);
            Assert.Equal(HastaUtc, hastaUtc);
            return Task.FromResult(Resumen);
        }

        public Task<IReadOnlyList<OcupacionPuesto>> ObtenerOcupacionAsync(
            Guid lavaderoId,
            Guid? puestoId,
            DateTime desdeUtc,
            DateTime hastaUtc,
            CancellationToken ct = default)
        {
            Assert.Equal(LavaderoId, lavaderoId);
            return Task.FromResult(Ocupacion);
        }
    }
}
