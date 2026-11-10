using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WashPoints.Api.Hubs;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-196 (slice-18, D-18.3/D-18.4): el publicador deriva el aviso de métricas
/// de las filas que ya existen y lo envía a un grupo administrativo aparte.
///
/// La pata de métricas es <b>best-effort</b>: si falla, no impide publicar
/// disponibilidad ni agenda y no deja el lote sin marcar. Se prueba con un
/// <see cref="IHubContext{THub}"/> falso y la base real: el publicador es la
/// unidad bajo test, no el pipeline HTTP.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class PublicadorMetricasTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly Guid LavaderoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TurnoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PuestoId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly ContenedorPostgres _baseDeDatos;

    public PublicadorMetricasTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("TRUNCATE TABLE outbox CASCADE;");

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    /// <summary>
    /// Turno a las 22:30 locales del 10/11/2026: en UTC cae el 11/11 a las
    /// 01:30. La fecha del evento tiene que ser la local, no la UTC.
    /// </summary>
    private static DateTime InicioLocalTarde => ConversorHorario.AUtc(new DateTime(2026, 11, 10, 22, 30, 0));

    private static EventoOcupacion Ocupacion(DateTime desdeUtc) => new(
        TurnoId, LavaderoId, PuestoId, OrigenTurno.App, EstadoTurno.Confirmado,
        desdeUtc, desdeUtc.AddMinutes(30));

    private static EventoAgenda Agenda(DateTime inicioUtc) => new(
        TurnoId, LavaderoId, PuestoId, OrigenTurno.App, EstadoTurno.Confirmado,
        inicioUtc, inicioUtc.AddMinutes(30));

    [Fact]
    public async Task UnCambioDeTurnoEmiteMetricasConLaFechaLocalSinAgregarFilas()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, TimeProvider.System);
        repositorio.Encolar(Ocupacion(InicioLocalTarde));
        repositorio.Encolar(Agenda(InicioLocalTarde));
        await contexto.SaveChangesAsync();

        var hub = new HubFalso();
        var publicador = new WashPoints.Api.PublicadorOutbox(
            contexto, hub, TimeProvider.System, NullLogger<WashPoints.Api.PublicadorOutbox>.Instance);

        await publicador.PublicarPendientesAsync(CancellationToken.None);

        var metricas = hub.EnviosA("metricas-").Select(e => Assert.IsType<EventoMetricas>(e.Argumento)).ToList();
        Assert.Equal(2, metricas.Count);
        Assert.All(metricas, m =>
        {
            Assert.Equal(LavaderoId, m.LavaderoId);
            Assert.Equal(new DateOnly(2026, 11, 10), m.Fecha);
        });

        // CA-18.5: el fan-out no escribe filas nuevas en la outbox.
        Assert.Equal(2, await contexto.Outbox.CountAsync());
        Assert.All(await contexto.Outbox.ToListAsync(), fila => Assert.NotNull(fila.PublicadoEn));
    }

    [Fact]
    public async Task UnCambioDeCapacidadSeEmiteConFechaNula()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, TimeProvider.System);
        repositorio.Encolar(new EventoMetricas(LavaderoId, null, DateTime.UtcNow));
        await contexto.SaveChangesAsync();

        var hub = new HubFalso();
        var publicador = new WashPoints.Api.PublicadorOutbox(
            contexto, hub, TimeProvider.System, NullLogger<WashPoints.Api.PublicadorOutbox>.Instance);

        await publicador.PublicarPendientesAsync(CancellationToken.None);

        var envio = Assert.Single(hub.Envios);
        Assert.Equal($"metricas-{LavaderoId}", envio.Grupo);
        Assert.Equal("metricasActualizadas", envio.Metodo);
        var evento = Assert.IsType<EventoMetricas>(envio.Argumento);
        Assert.Null(evento.Fecha);
        Assert.NotNull((await contexto.Outbox.SingleAsync()).PublicadoEn);
    }

    [Fact]
    public async Task UnFalloDelCanalDeMetricasNoImpideDisponibilidadNiAgenda()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, TimeProvider.System);
        repositorio.Encolar(Ocupacion(InicioLocalTarde));
        repositorio.Encolar(Agenda(InicioLocalTarde));
        await contexto.SaveChangesAsync();

        var hub = new HubFalso { GruposQueFallan = { "metricas-" } };
        var publicador = new WashPoints.Api.PublicadorOutbox(
            contexto, hub, TimeProvider.System, NullLogger<WashPoints.Api.PublicadorOutbox>.Instance);

        await publicador.PublicarPendientesAsync(CancellationToken.None);

        Assert.Single(hub.EnviosA($"lavadero-{LavaderoId}"));
        Assert.Single(hub.EnviosA($"agenda-{LavaderoId}"));
    }

    [Fact]
    public async Task UnFalloDelCanalDeMetricasNoDejaElLoteSinMarcar()
    {
        await using var contexto = CrearContexto();
        var repositorio = new OutboxRepositorio(contexto, TimeProvider.System);
        repositorio.Encolar(Ocupacion(InicioLocalTarde));
        repositorio.Encolar(Agenda(InicioLocalTarde));
        await contexto.SaveChangesAsync();

        var hub = new HubFalso { GruposQueFallan = { "metricas-" } };
        var publicador = new WashPoints.Api.PublicadorOutbox(
            contexto, hub, TimeProvider.System, NullLogger<WashPoints.Api.PublicadorOutbox>.Instance);

        await publicador.PublicarPendientesAsync(CancellationToken.None);

        Assert.All(await contexto.Outbox.ToListAsync(), fila => Assert.NotNull(fila.PublicadoEn));
    }

    private sealed record Envio(string Grupo, string Metodo, object? Argumento);

    /// <summary>
    /// Hub falso que registra los envíos. Sólo implementa lo que el publicador
    /// usa (<c>Clients.Group(...).SendAsync(...)</c>); el resto del contrato de
    /// SignalR no participa del test.
    /// </summary>
    private sealed class HubFalso : IHubContext<DisponibilidadHub>
    {
        private readonly ClientesFalsos _clientes;

        public HubFalso() => _clientes = new ClientesFalsos(this);

        public List<Envio> Envios { get; } = new();

        public HashSet<string> GruposQueFallan { get; } = new(StringComparer.Ordinal);

        public IEnumerable<Envio> EnviosA(string prefijo) =>
            Envios.Where(e => e.Grupo.StartsWith(prefijo, StringComparison.Ordinal));

        public IHubClients Clients => _clientes;

        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class ClientesFalsos : IHubClients
        {
            private readonly HubFalso _dueno;

            public ClientesFalsos(HubFalso dueno) => _dueno = dueno;

            public IClientProxy Group(string groupName) => new ProxyFalso(_dueno, groupName);

            public IClientProxy All => throw new NotSupportedException();

            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
                throw new NotSupportedException();

            public IClientProxy Client(string connectionId) => throw new NotSupportedException();

            public IClientProxy Clients(IReadOnlyList<string> connectionIds) =>
                throw new NotSupportedException();

            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
                throw new NotSupportedException();

            public IClientProxy Groups(IReadOnlyList<string> groupNames) =>
                throw new NotSupportedException();

            public IClientProxy User(string userId) => throw new NotSupportedException();

            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class ProxyFalso : IClientProxy
        {
            private readonly HubFalso _dueno;
            private readonly string _grupo;

            public ProxyFalso(HubFalso dueno, string grupo)
            {
                _dueno = dueno;
                _grupo = grupo;
            }

            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (_dueno.GruposQueFallan.Any(g => _grupo.StartsWith(g, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"Fallo simulado en {_grupo}.");
                }

                _dueno.Envios.Add(new Envio(_grupo, method, args.SingleOrDefault()));
                return Task.CompletedTask;
            }
        }
    }
}
