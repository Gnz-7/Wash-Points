using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-46: agenda del lavadero (CU-06, US-A05) end-to-end sobre HTTP.
///
/// CA-S4.6 — la agenda lista los turnos ordenados por hora con su estado real.
/// CA-S4.7 — la agenda filtra por puesto y estado.
/// CA-S4.16 — un turno de otro lavadero no aparece en la agenda (aislamiento, D-04.7).
///
/// Ver SPECS/slice-04-agenda-y-puestos.md T-46 y §5.1.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class AgendaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public AgendaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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
                // Los workers tickean solos; la agenda no depende de ellos y así
                // el test queda determinista.
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

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId)> SembrarCatalogoAsync(string nombre)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear(nombre);
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    private async Task<Guid> SembrarPuestoAsync(Guid lavaderoId, string nombre)
    {
        await using var contexto = CrearContexto();
        var puesto = Puesto.Crear(lavaderoId, nombre);
        contexto.Puestos.Add(puesto);
        await contexto.SaveChangesAsync();
        return puesto.Id;
    }

    private async Task<Guid> SembrarTurnoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoId, string inicioLocal, EstadoTurno estado)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse(inicioLocal));
        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);

        if (estado is EstadoTurno.Confirmado or EstadoTurno.EnCurso)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
        }

        if (estado == EstadoTurno.EnCurso)
        {
            turno.TransicionarA(EstadoTurno.EnCurso);
        }

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno.Id;
    }

    [Fact]
    public async Task AgendaOrdenaPorHora()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Central");
        var tarde = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T10:00:00", EstadoTurno.Confirmado);
        var temprano = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T08:00:00", EstadoTurno.Confirmado);
        var medio = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T09:00:00", EstadoTurno.Confirmado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var agenda = await cliente.GetFromJsonAsync<List<RespuestaAgenda>>(
            $"/api/lavaderos/{lavaderoId}/agenda?fecha=2026-10-10");

        Assert.NotNull(agenda);
        Assert.Equal(3, agenda!.Count);
        Assert.Equal([temprano, medio, tarde], agenda.Select(t => t.turnoId).ToArray());
        Assert.All(agenda, t => Assert.Equal("confirmado", t.estado));
        Assert.All(agenda, t => Assert.Equal("Exterior", t.tipoLavado));
        Assert.All(agenda, t => Assert.Equal("Puesto 1", t.puesto));
    }

    [Fact]
    public async Task AgendaFiltraPorPuestoYEstado()
    {
        var (lavaderoId, puesto1, tipoId) = await SembrarCatalogoAsync("Lavadero Filtros");
        var puesto2 = await SembrarPuestoAsync(lavaderoId, "Puesto 2");

        await SembrarTurnoAsync(lavaderoId, puesto1, tipoId, "2026-10-10T10:00:00", EstadoTurno.Confirmado);
        var enCurso = await SembrarTurnoAsync(lavaderoId, puesto1, tipoId, "2026-10-10T11:00:00", EstadoTurno.EnCurso);
        await SembrarTurnoAsync(lavaderoId, puesto2, tipoId, "2026-10-10T12:00:00", EstadoTurno.Confirmado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var porPuesto = await cliente.GetFromJsonAsync<List<RespuestaAgenda>>(
            $"/api/lavaderos/{lavaderoId}/agenda?fecha=2026-10-10&puestoId={puesto1}");
        Assert.NotNull(porPuesto);
        Assert.Equal(2, porPuesto!.Count);
        Assert.All(porPuesto, t => Assert.Equal(puesto1, t.puestoId));

        var porEstado = await cliente.GetFromJsonAsync<List<RespuestaAgenda>>(
            $"/api/lavaderos/{lavaderoId}/agenda?fecha=2026-10-10&estado=en_curso");
        Assert.NotNull(porEstado);
        var unico = Assert.Single(porEstado!);
        Assert.Equal(enCurso, unico.turnoId);
    }

    [Fact]
    public async Task AgendaNoVeOtroLavadero()
    {
        var (lavaderoA, puestoA, tipoA) = await SembrarCatalogoAsync("Lavadero A");
        var (lavaderoB, puestoB, tipoB) = await SembrarCatalogoAsync("Lavadero B");
        var turnoA = await SembrarTurnoAsync(lavaderoA, puestoA, tipoA, "2026-10-10T10:00:00", EstadoTurno.Confirmado);
        await SembrarTurnoAsync(lavaderoB, puestoB, tipoB, "2026-10-10T10:00:00", EstadoTurno.Confirmado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var agenda = await cliente.GetFromJsonAsync<List<RespuestaAgenda>>(
            $"/api/lavaderos/{lavaderoA}/agenda?fecha=2026-10-10");

        Assert.NotNull(agenda);
        var unico = Assert.Single(agenda!);
        Assert.Equal(turnoA, unico.turnoId);
        Assert.Equal(puestoA, unico.puestoId);
    }

    [Fact]
    public async Task FechaInvalidaDevuelve400()
    {
        var (lavaderoId, _, _) = await SembrarCatalogoAsync("Lavadero Fecha");
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/agenda?fecha=no-es-fecha");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    private sealed record RespuestaAgenda(
        Guid turnoId,
        Guid puestoId,
        string puesto,
        string tipoLavado,
        string origen,
        string estado);
}
