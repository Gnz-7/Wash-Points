using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Ocupacion;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-193/T-195 (slice-18, D-18.2/D-18.3): la outbox sabe persistir el evento
/// de métricas con su propio tipo y un payload sin importes ni PII.
///
/// El payload de <c>metricas_cambiada</c> sólo lleva <c>lavaderoId</c>,
/// <c>fecha</c> (día local, nullable) y <c>generadoEn</c>: es una señal de
/// invalidación, no un dato. Un cambio de capacidad viaja con <c>fecha</c>
/// nula; el de un turno, con el día local del turno.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class OutboxMetricasTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly Guid LavaderoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Ahora = new(2026, 11, 10, 18, 22, 31, DateTimeKind.Utc);

    private readonly ContenedorPostgres _baseDeDatos;

    public OutboxMetricasTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("TRUNCATE TABLE outbox CASCADE;");

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    [Fact]
    public async Task UnCambioDeCapacidadSeEncolaConFechaNula()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, new RelojFijo(Ahora));

        repositorio.Encolar(new EventoMetricas(LavaderoId, null, Ahora));
        await contexto.SaveChangesAsync();

        var fila = await contexto.Outbox.SingleAsync();
        Assert.Equal("metricas_cambiada", fila.Tipo);
        Assert.Null(fila.PublicadoEn);

        using var payload = JsonDocument.Parse(fila.Payload);
        var raiz = payload.RootElement;
        Assert.Equal(LavaderoId, raiz.GetProperty("lavaderoId").GetGuid());
        Assert.Equal(JsonValueKind.Null, raiz.GetProperty("fecha").ValueKind);
        Assert.Equal(Ahora, raiz.GetProperty("generadoEn").GetDateTime());
    }

    [Fact]
    public async Task UnCambioDeTurnoSeEncolaConLaFechaLocal()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, new RelojFijo(Ahora));

        repositorio.Encolar(new EventoMetricas(LavaderoId, new DateOnly(2026, 11, 10), Ahora));
        await contexto.SaveChangesAsync();

        var fila = await contexto.Outbox.SingleAsync();
        using var payload = JsonDocument.Parse(fila.Payload);

        Assert.Equal("2026-11-10", payload.RootElement.GetProperty("fecha").GetString());
    }

    [Fact]
    public async Task ElPayloadNoLlevaImportesNiConteos()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, new RelojFijo(Ahora));

        repositorio.Encolar(new EventoMetricas(LavaderoId, new DateOnly(2026, 11, 10), Ahora));
        await contexto.SaveChangesAsync();

        var fila = await contexto.Outbox.SingleAsync();
        using var payload = JsonDocument.Parse(fila.Payload);

        var campos = payload.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "lavaderoId", "fecha", "generadoEn" }, campos);
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;

        public RelojFijo(DateTime ahora) => _ahora = new DateTimeOffset(ahora);

        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
