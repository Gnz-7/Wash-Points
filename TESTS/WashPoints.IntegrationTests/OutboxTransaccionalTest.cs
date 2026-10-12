using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashPoints.Api;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-37: la outbox transaccional en las tres vías que cambian la disponibilidad
/// (SPECS/slice-03).
///
/// CA-S3.3 — la fila de `outbox` se escribe en el mismo commit que el turno
/// (espontánea, confirmación de reserva, expiración).
/// CA-S3.8 — la transición a `expirado` encola el evento de liberación.
/// Ventana cerrada — si la transacción falla, no queda fila de `outbox`.
///
/// Ver SPECS/slice-03-sincronizacion-rb2.md §5 (T-37) y §6.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class OutboxTransaccionalTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

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

    private WebApplicationFactory<Program> CrearApi(bool conClaveCifrado = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            if (conClaveCifrado)
            {
                builder.UseSetting(
                    "Comprobantes:ClaveCifrado",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef")));
            }

            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(new PasarelaPagosFakeSimulada());

                // El worker de reservas ejecuta un tick inmediato al arrancar y
                // competiría con las expiraciones que el test dispara a mano;
                // acá se controla la expiración de forma explícita.
                var worker = servicios.SingleOrDefault(
                    d => d.ImplementationType == typeof(WorkerReservas));
                if (worker is not null)
                {
                    servicios.Remove(worker);
                }
            });
        });

    private async Task<long> ContarOutboxAsync() =>
        await _baseDeDatos.ConsultarEscalarAsync<long>("SELECT COUNT(*) FROM outbox");

    [Fact]
    public async Task DemandaEspontaneaEscribeUnaFilaOutboxNoPublicada()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            new { puestoId, tipoLavadoId = tipoId, inicio = "2026-10-10T10:00:00" });

        Assert.Equal(System.Net.HttpStatusCode.Created, respuesta.StatusCode);

        Assert.Equal(1, await ContarOutboxAsync());
        var publicado = await _baseDeDatos.ConsultarEscalarAsync<string>(
            "SELECT publicado_en::text FROM outbox");
        Assert.Null(publicado);

        var payload = await _baseDeDatos.ConsultarEscalarAsync<string>(
            "SELECT payload::text FROM outbox");
        Assert.Contains(puestoId.ToString(), payload);
    }

    [Fact]
    public async Task ConfirmacionDeReservaEscribeFilaOutbox()
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
        Assert.Equal(1, await ContarOutboxAsync());
    }

    [Fact]
    public async Task ExpiracionEscribeEventoDeLiberacion()
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
        Assert.Equal(1, await ContarOutboxAsync());
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
