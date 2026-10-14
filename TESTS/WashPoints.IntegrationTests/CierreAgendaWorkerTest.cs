using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WashPoints.Application.Agenda;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-47: ventana de llegada (CU-06, US-A06 CA-6.4). Un turno <c>confirmado</c>
/// cuyo inicio ya pasó más allá de la ventana se marca <c>no_presentado</c> y
/// libera el puesto; dentro de la ventana no se toca.
///
/// CA-S4.11 — confirmado con ventana vencida ⇒ no_presentado y libera el puesto.
/// D-04.2 — el canje (en_curso) gana la carrera: no se marca.
///
/// Ver SPECS/slice-04-agenda-y-puestos.md T-47.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CierreAgendaWorkerTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string Ocupacion = "ocupacion_cambiada";
    private const string Agenda = "agenda_cambiada";

    private readonly ContenedorPostgres _baseDeDatos;

    public CierreAgendaWorkerTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private WebApplicationFactory<Program> CrearApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                foreach (var tipo in new[]
                         {
                             typeof(WashPoints.Api.WorkerReservas),
                             typeof(WashPoints.Api.PublicadorOutboxWorker),
                             typeof(WashPoints.Api.WorkerAgenda),
                         })
                {
                    var descriptor = servicios.SingleOrDefault(d => d.ImplementationType == tipo);
                    if (descriptor is not null)
                    {
                        servicios.Remove(descriptor);
                    }
                }
            });
        });

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId)> SembrarCatalogoAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Cierre");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    private async Task<Guid> SembrarTurnoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoId, DateTime inicioUtc, EstadoTurno estado)
    {
        await using var contexto = CrearContexto();

        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);

        if (estado == EstadoTurno.EnCurso)
        {
            turno.TransicionarA(EstadoTurno.EnCurso);
        }

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno.Id;
    }

    private static async Task<int> EjecutarTickAsync(WebApplicationFactory<Program> api)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        var servicio = alcance.ServiceProvider.GetRequiredService<CierreAgendaServicio>();
        return await servicio.EjecutarTickAsync(DateTime.UtcNow, CancellationToken.None);
    }

    private async Task<EstadoTurno> EstadoAsync(Guid turnoId)
    {
        await using var contexto = CrearContexto();
        return (await contexto.Turnos.SingleAsync(t => t.Id == turnoId)).Estado;
    }

    private async Task<long> ContarOutboxPorTipoAsync(string tipo) =>
        await _baseDeDatos.ConsultarEscalarAsync<long>(
            $"SELECT COUNT(*) FROM outbox WHERE tipo = '{tipo}'");

    [Fact]
    public async Task ConfirmadoConVentanaVencidaPasaANoPresentado()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        var inicioUtc = DateTime.UtcNow.AddMinutes(-30);
        var turnoId = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, inicioUtc, EstadoTurno.Confirmado);
        using var api = CrearApi();

        var cerrados = await EjecutarTickAsync(api);

        Assert.Equal(1, cerrados);
        Assert.Equal(EstadoTurno.NoPresentado, await EstadoAsync(turnoId));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));

        using var cliente = api.CreateClient();
        var inicioLocal = ConversorHorario.ALocal(inicioUtc);
        var desde = inicioLocal.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss");
        var hasta = inicioLocal.AddHours(2).ToString("yyyy-MM-ddTHH:mm:ss");
        var disponibilidad = await cliente.GetFromJsonAsync<List<Intervalo>>(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde={desde}&hasta={hasta}");
        Assert.NotNull(disponibilidad);
        Assert.DoesNotContain(disponibilidad!, o => o.turnoId == turnoId);
    }

    [Fact]
    public async Task ConfirmadoDentroDeLaVentanaNoSeToca()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        var turnoId = await SembrarTurnoAsync(
            lavaderoId, puestoId, tipoId, DateTime.UtcNow.AddMinutes(5), EstadoTurno.Confirmado);
        using var api = CrearApi();

        var cerrados = await EjecutarTickAsync(api);

        Assert.Equal(0, cerrados);
        Assert.Equal(EstadoTurno.Confirmado, await EstadoAsync(turnoId));
        Assert.Equal(0, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(0, await ContarOutboxPorTipoAsync(Agenda));
    }

    [Fact]
    public async Task CanjeAntesDelWorkerGanaLaCarrera()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        var turnoId = await SembrarTurnoAsync(
            lavaderoId, puestoId, tipoId, DateTime.UtcNow.AddMinutes(-30), EstadoTurno.EnCurso);
        using var api = CrearApi();

        var cerrados = await EjecutarTickAsync(api);

        Assert.Equal(0, cerrados);
        Assert.Equal(EstadoTurno.EnCurso, await EstadoAsync(turnoId));
    }

    private sealed record Intervalo(Guid turnoId, Guid puestoId, string origen);
}
