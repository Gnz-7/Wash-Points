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
/// T-48: tiempo real de la agenda (CU-06, US-A05 CA-5.4).
///
/// CA-S4.8 — una conexión suscripta al grupo <c>agenda-{id}</c> recibe
///           <c>agendaActualizada</c> al crear o finalizar un turno.
/// CA-S4.16 — el evento de otro lavadero no llega; el canal de agenda es
///            independiente del de disponibilidad.
///
/// El publicador se dispara a mano para que el test sea determinista (los
/// workers se remueven del host). Ver SPECS/slice-04-agenda-y-puestos.md T-48.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class AgendaSignalRTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public AgendaSignalRTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
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

    private async Task<Guid> SembrarTurnoEnCursoAsync(Guid lavaderoId, Guid puestoId, Guid tipoId)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse("2026-10-10T10:00:00"));
        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);
        turno.TransicionarA(EstadoTurno.EnCurso);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno.Id;
    }

    private async Task<HubConnection> ConectarAsync(CustomWebApplicationFactory api, Guid lavaderoId)
    {
        var conexion = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "hubs/disponibilidad"), opciones =>
            {
                opciones.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                opciones.Transports = HttpTransportType.LongPolling;
                opciones.AccessTokenProvider = () => Task.FromResult<string?>(api.TokenAdministrador(lavaderoId));
            })
            .Build();

        await conexion.StartAsync();
        return conexion;
    }

    private static async Task PublicarAsync(CustomWebApplicationFactory api)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        var publicador = alcance.ServiceProvider.GetRequiredService<WashPoints.Api.PublicadorOutbox>();
        await publicador.PublicarPendientesAsync(CancellationToken.None);
    }

    private static object Cuerpo(Guid puestoId, Guid tipoId) => new
    {
        puestoId,
        tipoLavadoId = tipoId,
        inicio = "2026-10-10T10:00:00",
    };

    [Fact]
    public async Task ClienteDeAgendaRecibeAgendaActualizadaAlCrear()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Agenda");
        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, lavaderoId);

        var agenda = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disponibilidad = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<string>("agendaActualizada", payload => agenda.TrySetResult(payload));
        conexion.On<string>("disponibilidadActualizada", payload => disponibilidad.TrySetResult(payload));

        await conexion.InvokeAsync("UnirseAAgenda", lavaderoId);

        using var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos", Cuerpo(puestoId, tipoId));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        await PublicarAsync(api);

        var payload = await agenda.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(puestoId.ToString(), payload);

        // El canal de agenda es independiente: la conexión no está en el grupo
        // de disponibilidad, así que no debe recibir ese evento.
        await Task.Delay(500);
        Assert.False(disponibilidad.Task.IsCompleted, "No debe llegar disponibilidadActualizada al grupo de agenda.");
    }

    [Fact]
    public async Task FinalizarEmiteAgendaActualizada()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Agenda Finalizar");
        var turnoId = await SembrarTurnoEnCursoAsync(lavaderoId, puestoId, tipoId);

        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, lavaderoId);

        var agenda = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<string>("agendaActualizada", payload => agenda.TrySetResult(payload));

        await conexion.InvokeAsync("UnirseAAgenda", lavaderoId);

        using var cliente = api.CreateClient();
        var finalizar = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/turnos/{turnoId}/finalizar", null);
        Assert.Equal(HttpStatusCode.OK, finalizar.StatusCode);

        await PublicarAsync(api);

        var payload = await agenda.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(turnoId.ToString(), payload);
    }

    [Fact]
    public async Task EventoDeOtroLavaderoNoLlegaAlGrupoDeAgenda()
    {
        var (lavaderoA, _, _) = await SembrarCatalogoAsync("Lavadero A");
        var (lavaderoB, puestoB, tipoB) = await SembrarCatalogoAsync("Lavadero B");

        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, lavaderoA);

        var recibido = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<string>("agendaActualizada", payload => recibido.TrySetResult(payload));

        await conexion.InvokeAsync("UnirseAAgenda", lavaderoA);

        using var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoB}/turnos-espontaneos", Cuerpo(puestoB, tipoB));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        await PublicarAsync(api);
        await Task.Delay(1000);

        Assert.False(recibido.Task.IsCompleted, "Un evento de otro lavadero no debe llegar al grupo de agenda.");
    }
}
