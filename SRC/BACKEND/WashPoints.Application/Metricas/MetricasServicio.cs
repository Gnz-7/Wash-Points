using WashPoints.Domain.Metricas;

namespace WashPoints.Application.Metricas;

/// <summary>
/// Caso de uso de consulta de métricas (CU-09). Es de solo lectura: compone el
/// resumen del día y calcula la tasa de ocupación, pero no escribe en el modelo
/// operativo (ARCHITECTURE §2.2, CA-S6.7).
/// El día local lo resuelve la API con <c>ConversorHorario</c> y lo entrega ya
/// convertido a UTC, para no invertir la regla de dependencias entre capas.
/// </summary>
public sealed class MetricasServicio
{
    private const string Moneda = "ARS";

    private readonly IMetricasConsulta _consulta;

    public MetricasServicio(IMetricasConsulta consulta)
    {
        _consulta = consulta;
    }

    public async Task<ResumenDiario> ObtenerResumenAsync(
        Guid lavaderoId,
        DateOnly fecha,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct = default)
    {
        ValidarVentana(desdeUtc, hastaUtc);

        var resumen = await _consulta.ObtenerResumenAsync(lavaderoId, desdeUtc, hastaUtc, ct);

        return new ResumenDiario(
            fecha,
            new CapacidadInstalada(
                resumen.PuestosTotal,
                resumen.PuestosActivos,
                resumen.PuestosOcupados,
                resumen.CalculadoEnUtc),
            new IngresosDelDia(
                RedondearImporte(resumen.SenasCobradas),
                RedondearImporte(resumen.FacturadoTotal),
                RedondearImporte(resumen.FacturadoMostrador),
                Moneda),
            new ConteoTurnos(
                resumen.Atendidos,
                resumen.Espontaneos,
                resumen.NoPresentados,
                resumen.Cancelaciones));
    }

    public async Task<OcupacionDelDia> ObtenerOcupacionAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        Guid? puestoId = null,
        CancellationToken ct = default)
    {
        ValidarVentana(desdeUtc, hastaUtc);

        var minutosVentana = (int)Math.Round(
            (hastaUtc - desdeUtc).TotalMinutes,
            MidpointRounding.AwayFromZero);

        var puestos = await _consulta.ObtenerOcupacionAsync(
            lavaderoId,
            puestoId,
            desdeUtc,
            hastaUtc,
            ct);

        var proyectados = puestos
            .Select(puesto => new OcupacionDePuesto(
                puesto.PuestoId,
                puesto.Nombre,
                puesto.MinutosOcupados,
                minutosVentana,
                CalcularTasa(puesto.MinutosOcupados, minutosVentana)))
            .ToArray();

        return new OcupacionDelDia(desdeUtc, hastaUtc, proyectados);
    }

    private static void ValidarVentana(DateTime desdeUtc, DateTime hastaUtc)
    {
        if (hastaUtc <= desdeUtc)
        {
            throw new VentanaInvalidaException();
        }
    }

    private static decimal CalcularTasa(int minutosOcupados, int minutosVentana)
    {
        if (minutosVentana <= 0)
        {
            return 0m;
        }

        var tasa = (decimal)minutosOcupados / minutosVentana;
        return Math.Round(tasa, 4, MidpointRounding.AwayFromZero);
    }

    private static decimal RedondearImporte(decimal importe) =>
        Math.Round(importe, 2, MidpointRounding.AwayFromZero);
}
