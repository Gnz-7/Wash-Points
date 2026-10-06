using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-07: el endpoint HTTP de disponibilidad (CA-S0.5 y la dirección "exponer"
/// de T-09).
///
/// Un turno guardado a las 13:00 UTC (hora local del lavadero 10:00) aparece en
/// la respuesta como 10:00 local. Solo se muestran turnos que ocupan puesto:
/// <c>confirmado</c> y <c>en_curso</c>; los <c>pendiente_pago</c> no (RB-2).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class EndpointDisponibilidadTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public EndpointDisponibilidadTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas()
    {
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);
    }

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private WebApplicationFactory<Program> CrearApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
        });

    private async Task<(Guid LavaderoId, Guid PuestoId)> SembrarGuardada()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        // 13:00 UTC == 10:00 en Buenos Aires. El turno guardado debe exponerse
        // al cliente como "10:00" (T-09, dirección exponer).
        var confirmado = Confirmado(
            lavadero.Id, puesto.Id, tipo.Id, cliente.Id,
            new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc));
        var enCurso = Confirmado(
            lavadero.Id, puesto.Id, tipo.Id, cliente.Id,
            new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc));
        enCurso.TransicionarA(EstadoTurno.EnCurso);

        // Pendiente: no ocupa puesto, no debería aparecer (RB-2 + CA-S0.5).
        var pendiente = Turno.Crear(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 16, 0, 0, DateTimeKind.Utc),
            precioSena: 3000m);

        contexto.Turnos.AddRange(confirmado, enCurso, pendiente);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id);
    }

    private static Turno Confirmado(Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, Guid clienteId, DateTime inicioUtc, DateTime finUtc)
    {
        var turno = Turno.Crear(lavaderoId, puestoId, clienteId, tipoLavadoId, inicioUtc, finUtc, precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);
        return turno;
    }

    [Fact]
    public async Task DisponibilidadDevuelveLasDiezLocalesParaLasTreceUtc()
    {
        var (lavaderoId, _) = await SembrarGuardada();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        // La ventana se manda en hora local de Argentina: de las 09:30 a las
        // 11:30 locales (== 12:30 a 14:30 UTC) captura al turno de las 10:00.
        var respuesta = await cliente.GetFromJsonAsync<List<RespuestaDisponibilidad>>(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T09:30:00&hasta=2026-10-10T11:00:00");

        Assert.NotNull(respuesta);
        var intervalo = Assert.Single(respuesta);
        Assert.Equal("2026-10-10T10:00:00", intervalo.desde);
        Assert.Equal("2026-10-10T11:00:00", intervalo.hasta);
        Assert.Equal("app", intervalo.origen);
    }

    [Fact]
    public async Task DisponibilidadNoIncluyeLosPendientesDePago()
    {
        var (lavaderoId, _) = await SembrarGuardada();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        // Ventana local amplia: de las 09:00 a las 14:00. Cuenta los tres
        // turnos de la siembra; solo los dos que ocupan puesto (confirmado y
        // en_curso, 10:00-12:00 local) deben aparecer.
        var respuesta = await cliente.GetFromJsonAsync<List<RespuestaDisponibilidad>>(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T09:00:00&hasta=2026-10-10T14:00:00");

        Assert.NotNull(respuesta);
        Assert.Equal(2, respuesta.Count);
        Assert.Equal(new[] { "10:00:00", "11:00:00" }, respuesta.Select(i => i.desde.Split('T')[1]).ToArray());
    }

    private sealed record RespuestaDisponibilidad(
        Guid turnoId,
        Guid puestoId,
        string origen,
        string desde,
        string hasta);
}