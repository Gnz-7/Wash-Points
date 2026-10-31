using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashPoints.Api;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Comprobantes;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-37/T-45: la outbox transaccional en TODAS las vías que cambian la
/// disponibilidad o la agenda (SPECS/slice-03 y SPECS/slice-04 §5.3).
///
/// Regla (RB-2): un evento de ocupación se emite cuando cambia el predicado de
/// disponibilidad; un evento de agenda, en todo cambio de estado. Ambos viajan en
/// el mismo <c>SaveChangesAsync</c> que el turno.
///
/// Matriz §5.3 cubierta:
///   reserva creada → ocupación + agenda
///   confirmación   → ocupación + agenda
///   pago rechazado → ocupación + agenda
///   expiración     → ocupación + agenda
///   canje          → solo agenda
///
/// Ver SPECS/slice-04-agenda-y-puestos.md T-45 y §5.3.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class OutboxTransaccionalTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string Ocupacion = "ocupacion_cambiada";
    private const string Agenda = "agenda_cambiada";

    private static readonly string ClaveCifrado =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly GeneradorComprobante Generador = new();

    private readonly ContenedorPostgres _baseDeDatos;
    private readonly CifradorComprobanteAesGcm _cifrador = new(ClaveCifrado);

    public OutboxTransaccionalTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId)> SembrarCatalogoAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Outbox");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    /// <summary>Siembra un pendiente_pago con el vencimiento indicado.</summary>
    private async Task<Guid> SembrarTurnoPendienteAsync(DateTime expiraEnUtc)
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        await using var contexto = CrearContexto();

        var inicio = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var cliente = Cliente.Crear("Cliente Outbox");
        var turno = Turno.CrearParaReserva(
            lavaderoId, puestoId, cliente.Id, tipoId,
            inicio, inicio.AddMinutes(30), precioSena: 3000m,
            expiraEnUtc: expiraEnUtc, idempotencyKey: Guid.NewGuid().ToString("N"));

        contexto.AddRange(cliente, turno);
        await contexto.SaveChangesAsync();

        return turno.Id;
    }

    /// <summary>Siembra un turno confirmado con su comprobante listo para canjear.</summary>
    private async Task<(Guid TurnoId, Guid LavaderoId, string Qr)> SembrarTurnoConfirmadoConComprobanteAsync()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        await using var contexto = CrearContexto();

        var inicio = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var cliente = Cliente.Crear("Cliente Outbox");
        var turno = Turno.CrearParaReserva(
            lavaderoId, puestoId, cliente.Id, tipoId,
            inicio, inicio.AddMinutes(30), precioSena: 3000m,
            expiraEnUtc: DateTime.UtcNow.AddMinutes(5),
            idempotencyKey: Guid.NewGuid().ToString("N"));
        turno.TransicionarA(EstadoTurno.Confirmado);

        var token = Generador.GenerarToken();
        var comprobante = Comprobante.Emitir(
            turno.Id,
            Generador.CalcularHash(token),
            _cifrador.Cifrar(token),
            DateTime.UtcNow.AddHours(24));

        contexto.AddRange(cliente, turno);
        contexto.Comprobantes.Add(comprobante);
        await contexto.SaveChangesAsync();

        return (turno.Id, lavaderoId, Generador.ConstruirPayloadQr(token));
    }

    private CustomWebApplicationFactory CrearApi(bool conClaveCifrado = true) =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            if (conClaveCifrado)
            {
                builder.UseSetting("Comprobantes:ClaveCifrado", ClaveCifrado);
            }
            else
            {
                // Sin clave la emisión debe fallar. Se limpia el setting de
                // forma explícita: si no, el host en Development toma la clave
                // local de appsettings.Development.json y el test pierde su
                // determinismo (hallazgo de T-95).
                builder.UseSetting("Comprobantes:ClaveCifrado", string.Empty);
            }

            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(new PasarelaPagosFakeSimulada());

                // Los workers ejecutan un tick inmediato al arrancar y competirían
                // con las transiciones que el test dispara a mano; acá se
                // controlan de forma explícita.
                foreach (var tipo in new[]
                         {
                             typeof(WorkerReservas),
                             typeof(WashPoints.Api.WorkerAgenda),
                         })
                {
                    var worker = servicios.SingleOrDefault(
                        d => d.ImplementationType == tipo);
                    if (worker is not null)
                    {
                        servicios.Remove(worker);
                    }
                }
            });
        });

    private async Task<long> ContarOutboxAsync() =>
        await _baseDeDatos.ConsultarEscalarAsync<long>("SELECT COUNT(*) FROM outbox");

    private async Task<long> ContarOutboxPorTipoAsync(string tipo) =>
        await _baseDeDatos.ConsultarEscalarAsync<long>(
            $"SELECT COUNT(*) FROM outbox WHERE tipo = '{tipo}'");

    [Fact]
    public async Task DemandaEspontaneaEncolaOcupacionYAgenda()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            new { puestoId, tipoLavadoId = tipoId, inicio = "2026-10-10T10:00:00" });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        Assert.Equal(1, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));

        var publicado = await _baseDeDatos.ConsultarEscalarAsync<string>(
            "SELECT publicado_en::text FROM outbox WHERE tipo = 'ocupacion_cambiada'");
        Assert.Null(publicado);
    }

    [Fact]
    public async Task CreacionDeReservaEncolaOcupacionYAgenda()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            new
            {
                puestoId,
                tipoLavadoId = tipoId,
                clienteId = Guid.NewGuid(),
                inicio = "2026-10-10T10:00:00",
            });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));

        var publicadas = await _baseDeDatos.ConsultarEscalarAsync<long>(
            "SELECT COUNT(*) FROM outbox WHERE publicado_en IS NOT NULL");
        Assert.Equal(0, publicadas);
    }

    [Fact]
    public async Task ConfirmacionDeReservaEncolaOcupacionYAgenda()
    {
        var turnoId = await SembrarTurnoPendienteAsync(DateTime.UtcNow.AddMinutes(5));
        using var api = CrearApi();

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<WashPointsDbContext>();
            var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
            var aplicador = alcance.ServiceProvider.GetRequiredService<WebhookPagoServicio>();

            var resultado = await aplicador.AplicarVerificacionAsync(
                turno, "pago-outbox-1", EstadoVerificacionPago.Aprobado, CancellationToken.None);

            Assert.Equal(ResultadoWebhook.Procesado, resultado);
        }

        await using var verificacion = CrearContexto();
        Assert.Equal(EstadoTurno.Confirmado, (await verificacion.Turnos.SingleAsync(t => t.Id == turnoId)).Estado);
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));
    }

    [Fact]
    public async Task PagoRechazadoEncolaCancelacion()
    {
        var turnoId = await SembrarTurnoPendienteAsync(DateTime.UtcNow.AddMinutes(5));
        using var api = CrearApi();

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<WashPointsDbContext>();
            var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
            var aplicador = alcance.ServiceProvider.GetRequiredService<WebhookPagoServicio>();

            var resultado = await aplicador.AplicarVerificacionAsync(
                turno, "pago-rechazado-1", EstadoVerificacionPago.Rechazado, CancellationToken.None);

            Assert.Equal(ResultadoWebhook.Procesado, resultado);
        }

        await using var verificacion = CrearContexto();
        Assert.Equal(EstadoTurno.Cancelado, (await verificacion.Turnos.SingleAsync(t => t.Id == turnoId)).Estado);
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));
    }

    [Fact]
    public async Task ExpiracionEncolaAgendaAdemasDeOcupacion()
    {
        var turnoId = await SembrarTurnoPendienteAsync(DateTime.UtcNow.AddMinutes(-10));
        using var api = CrearApi();

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var expirador = alcance.ServiceProvider.GetRequiredService<RespaldoYExpiracionServicio>();
            var expirados = await expirador.ExpirarVencidosAsync(DateTime.UtcNow, CancellationToken.None);
            Assert.Equal(1, expirados);
        }

        await using var verificacion = CrearContexto();
        Assert.Equal(EstadoTurno.Expirado, (await verificacion.Turnos.SingleAsync(t => t.Id == turnoId)).Estado);
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Ocupacion));
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));
    }

    [Fact]
    public async Task CanjeEncolaAgendaSinOcupacion()
    {
        var (turnoId, lavaderoId, qr) = await SembrarTurnoConfirmadoConComprobanteAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/comprobantes/validar", new { comprobante = qr });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var verificacion = CrearContexto();
        Assert.Equal(EstadoTurno.EnCurso, (await verificacion.Turnos.SingleAsync(t => t.Id == turnoId)).Estado);
        Assert.Equal(1, await ContarOutboxPorTipoAsync(Agenda));
        Assert.Equal(0, await ContarOutboxPorTipoAsync(Ocupacion));
    }

    [Fact]
    public async Task SiLaTransaccionFallaNoQuedaFilaOutbox()
    {
        var turnoId = await SembrarTurnoPendienteAsync(DateTime.UtcNow.AddMinutes(5));
        using var api = CrearApi(conClaveCifrado: false);

        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var contexto = alcance.ServiceProvider.GetRequiredService<WashPointsDbContext>();
            var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
            var aplicador = alcance.ServiceProvider.GetRequiredService<WebhookPagoServicio>();

            // Sin clave de cifrado, la emisión del comprobante falla antes del
            // commit: ni pago ni confirmación ni evento quedan persistidos.
            await Assert.ThrowsAnyAsync<Exception>(() => aplicador.AplicarVerificacionAsync(
                turno, "pago-falla", EstadoVerificacionPago.Aprobado, CancellationToken.None));
        }

        await using var verificacion = CrearContexto();
        Assert.Equal(EstadoTurno.PendientePago, (await verificacion.Turnos.SingleAsync(t => t.Id == turnoId)).Estado);
        Assert.Equal(0, await ContarOutboxAsync());
    }
}
