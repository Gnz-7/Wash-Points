using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-199 (slice-18): contrato HTTP/SignalR del canal de métricas.
///
/// CA-18.2 — el administrador dueño unido a <c>metricas-{id}</c> recibe
///           <c>metricasActualizadas</c> con el lavadero y el día local.
/// CA-18.3 — un cliente y un administrador ajeno son rechazados con
///           <c>no_es_propietario</c> y no reciben el evento.
/// CA-18.4 — el canal de disponibilidad no recibe métricas (RB-2 intacta).
///
/// El publicador se dispara a mano: los workers se remueven del host.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class MetricasSignalRTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string Evento = "metricasActualizadas";

    private readonly ContenedorPostgres _baseDeDatos;

    public MetricasSignalRTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    private async Task<HubConnection> ConectarAsync(
        CustomWebApplicationFactory api,
        Func<string?> token)
    {
        var conexion = new HubConnectionBuilder()
            .WithUrl(new Uri(api.Server.BaseAddress, "hubs/disponibilidad"), opciones =>
            {
                opciones.HttpMessageHandlerFactory = _ => api.Server.CreateHandler();
                opciones.Transports = HttpTransportType.LongPolling;
                opciones.AccessTokenProvider = () => Task.FromResult(token());
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
        // Hora local del lavadero, independiente de la zona del host de tests.
        inicio = ConversorHorario.ALocal(DateTime.UtcNow)
            .ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static async Task<HttpResponseMessage> RegistrarEspontaneaAsync(
        CustomWebApplicationFactory api,
        Guid lavaderoId,
        Guid puestoId,
        Guid tipoId)
    {
        using var cliente = api.CreateClient();
        return await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos", Cuerpo(puestoId, tipoId));
    }

    [Fact]
    public async Task ElAdministradorRecibeMetricasActualizadasAlRegistrarUnaEspontanea()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Métricas");
        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, () => api.TokenAdministrador(lavaderoId));

        var recibido = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<JsonElement>(Evento, payload => recibido.TrySetResult(payload));

        await conexion.InvokeAsync("UnirseAMetricas", lavaderoId);

        var antes = DateOnly.FromDateTime(ConversorHorario.ALocal(DateTime.UtcNow));
        var alta = await RegistrarEspontaneaAsync(api, lavaderoId, puestoId, tipoId);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var despues = DateOnly.FromDateTime(ConversorHorario.ALocal(DateTime.UtcNow));

        await PublicarAsync(api);

        var payload = await recibido.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(lavaderoId, payload.GetProperty("lavaderoId").GetGuid());

        var fecha = DateOnly.Parse(payload.GetProperty("fecha").GetString()!);
        Assert.Contains(fecha, new[] { antes, despues });

        // D-18.2: la señal no lleva importes ni conteos.
        Assert.Equal(
            new[] { "lavaderoId", "fecha", "generadoEn" },
            payload.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task UnClienteNoPuedeUnirseAlCanalDeMetricas()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Métricas Cliente");
        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, () => api.TokenCliente());

        var recibido = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<JsonElement>(Evento, payload => recibido.TrySetResult(payload));

        var error = await Assert.ThrowsAsync<HubException>(
            () => conexion.InvokeAsync("UnirseAMetricas", lavaderoId));
        Assert.Contains("no_es_propietario", error.Message);

        var alta = await RegistrarEspontaneaAsync(api, lavaderoId, puestoId, tipoId);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        await PublicarAsync(api);
        await Task.Delay(500);

        Assert.False(recibido.Task.IsCompleted, "Un cliente no debe recibir métricas.");
    }

    [Fact]
    public async Task UnAdministradorAjenoNoPuedeUnirseAlCanalDeMetricas()
    {
        var (lavaderoA, puestoA, tipoA) = await SembrarCatalogoAsync("Lavadero Métricas A");
        var (lavaderoB, _, _) = await SembrarCatalogoAsync("Lavadero Métricas B");

        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, () => api.TokenAdministrador(lavaderoB));

        var error = await Assert.ThrowsAsync<HubException>(
            () => conexion.InvokeAsync("UnirseAMetricas", lavaderoA));
        Assert.Contains("no_es_propietario", error.Message);

        var recibido = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<JsonElement>(Evento, payload => recibido.TrySetResult(payload));

        var alta = await RegistrarEspontaneaAsync(api, lavaderoA, puestoA, tipoA);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        await PublicarAsync(api);
        await Task.Delay(500);

        Assert.False(recibido.Task.IsCompleted, "Un administrador ajeno no debe recibir métricas.");
    }

    [Fact]
    public async Task ElCanalDeDisponibilidadNoRecibeMetricas()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Métricas RB2");
        using var api = CrearApi();
        await using var conexion = await ConectarAsync(api, () => api.TokenAdministrador(lavaderoId));

        var metricas = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disponibilidad = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        conexion.On<JsonElement>(Evento, payload => metricas.TrySetResult(payload));
        conexion.On<string>("disponibilidadActualizada", payload => disponibilidad.TrySetResult(payload));

        // Sólo el canal de disponibilidad, como un conductor.
        await conexion.InvokeAsync("UnirseALavadero", lavaderoId);

        var alta = await RegistrarEspontaneaAsync(api, lavaderoId, puestoId, tipoId);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        await PublicarAsync(api);

        // RB-2 sigue funcionando y no se contamina con el canal administrativo.
        await disponibilidad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(500);
        Assert.False(metricas.Task.IsCompleted, "El grupo de disponibilidad no debe recibir métricas.");
    }
}
