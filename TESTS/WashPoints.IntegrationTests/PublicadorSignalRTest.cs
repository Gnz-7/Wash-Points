using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
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
/// T-38: sincronización en tiempo real por SignalR (RB-2, ARCHITECTURE §7.3).
///
/// CA-S3.4 — un cliente suscripto al grupo de su lavadero recibe el evento de
/// ocupación sin recargar; un evento de otro lavadero no le llega.
///
/// Ver SPECS/slice-03-sincronizacion-rb2.md §5 (T-38) y §6.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class PublicadorSignalRTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public PublicadorSignalRTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

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

    private async Task SembrarTurnoConfirmadoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoId, string inicioLocal)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse(inicioLocal));
        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
    }

    private WebApplicationFactory<Program> CrearApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                // Los workers tickean solos; acá se dispara el publicador a mano
                // para que el test sea determinista.
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

    private async Task<HubConnection> ConectarAsync(WebApplicationFactory<Program> api)
    {
        var conexion = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "hubs/disponibilidad"), opciones =>
            {
                opciones.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                opciones.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        await conexion.StartAsync();
        return conexion;
    }

    private static async Task PublicarAsync(WebApplicationFactory<Program> api)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        var publicador = alcance.ServiceProvider.GetRequiredService<WashPoints.Api.PublicadorOutbox>();
        await publicador.PublicarPendientesAsync(CancellationToken.None);
    }

    private static object Cuerpo(Guid puestoId, Guid tipoId, string inicioLocal) => new
    {
        puestoId,
        tipoLavadoId = tipoId,
        inicio = inicioLocal,
    };

    [Fact]
    public async Task ClienteDelGrupoRecibeEventoDeOcupacion()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero A");
        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api);

        var recibido = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<string>("disponibilidadActualizada", payload => recibido.TrySetResult(payload));

        await conexion.InvokeAsync("UnirseALavadero", lavaderoId);

        using var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            Cuerpo(puestoId, tipoId, "2026-10-10T10:00:00"));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        await PublicarAsync(api);

        var payload = await recibido.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(puestoId.ToString(), payload);
    }

    [Fact]
    public async Task EventoDeOtroLavaderoNoLlega()
    {
        var (lavaderoA, _, _) = await SembrarCatalogoAsync("Lavadero A");
        var (lavaderoB, puestoB, tipoB) = await SembrarCatalogoAsync("Lavadero B");
        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api);

        var recibido = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<string>("disponibilidadActualizada", payload => recibido.TrySetResult(payload));

        await conexion.InvokeAsync("UnirseALavadero", lavaderoA);

        using var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoB}/turnos-espontaneos",
            Cuerpo(puestoB, tipoB, "2026-10-10T10:00:00"));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        await PublicarAsync(api);
        await Task.Delay(1000);

        Assert.False(recibido.Task.IsCompleted, "Un evento de otro lavadero no debe llegar.");
    }

    [Fact]
    public async Task SolicitarSnapshotDevuelveOcupacionActual()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Snapshot");
        await SembrarTurnoConfirmadoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T10:00:00");

        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api);

        var snapshot = await conexion.InvokeAsync<List<IntervaloSnapshot>>(
            "SolicitarSnapshot",
            lavaderoId,
            new DateTime(2026, 10, 10, 9, 0, 0),
            new DateTime(2026, 10, 10, 12, 0, 0));

        var intervalo = Assert.Single(snapshot);
        Assert.Equal("app", intervalo.origen);
        Assert.Equal("2026-10-10T10:00:00", intervalo.desde);
    }

    private sealed record IntervaloSnapshot(
        Guid turnoId, Guid puestoId, string origen, string desde, string hasta);
}
