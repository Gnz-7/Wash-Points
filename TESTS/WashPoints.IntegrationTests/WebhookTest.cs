using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-17/T-18/T-19: el webhook IPN de Mercado Pago.
///
/// T-17 (CA-S1.4, RB-1): `approved` verificado confirma el turno y registra el
/// pago en un solo commit; nunca se confirma por `pending`/`in_process`.
/// T-18 (CA-S1.8): un webhook duplicado o fuera de orden responde `200` y no
/// duplica la fila de `pagos`.
/// T-19 (CA-S1.6): `rejected` cancela y libera; `pending` registra pero no
/// transiciona.
/// T-16 (CA-S1.5): firma inválida o fuera de tolerancia → `401` sin efectos.
///
/// Ver SPECS/slice-01-reserva-sena-mp.md §5 (T-17..T-19) y §6.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class WebhookTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string Secreto = "secreto-de-prueba-webhook";
    private static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    private readonly ContenedorPostgres _baseDeDatos;

    public WebhookTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<Guid> SembrarTurnoPendienteAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Webhook");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Webhook");

        var inicio = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), tipo.PrecioSena,
            DateTime.UtcNow.AddMinutes(5), "clave-de-prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        await contexto.SaveChangesAsync();

        return turno.Id;
    }

    private async Task<Guid> SembrarTurnoExpiradoAsync()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        await _baseDeDatos.EjecutarSqlAsync(
            $"UPDATE turnos SET estado = 'expirado' WHERE id = '{turnoId}'");
        return turnoId;
    }

    private WebApplicationFactory<Program> CrearApi(PasarelaPagosFakeSimulada pasarela) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.UseSetting("Pagos:WebhookSecret", Secreto);
            // Desde slice-02 la confirmación emite el comprobante (RB-3); la
            // clave de cifrado es obligatoria para poder confirmar.
            builder.UseSetting(
                "Comprobantes:ClaveCifrado",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef")));
            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(pasarela);
            });
        });

    private static object Cuerpo(string dataId) => new
    {
        type = "payment",
        action = "payment.created",
        data = new { id = dataId },
    };

    private static string Firmar(string dataId, string requestId, DateTimeOffset instante)
    {
        var ts = instante.ToUnixTimeSeconds();
        var manifiesto = $"id:{dataId};request-id:{requestId};ts:{ts};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secreto));
        var hash = Convert
            .ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifiesto)))
            .ToLowerInvariant();
        return $"ts={ts},v1={hash}";
    }

    private static void Firmar(HttpRequestMessage peticion, string dataId, TimeSpan? desfase = null)
    {
        var requestId = Guid.NewGuid().ToString("N");
        peticion.Headers.Add("x-request-id", requestId);
        peticion.Headers.Add(
            "x-signature",
            Firmar(dataId, requestId, DateTimeOffset.UtcNow + (desfase ?? TimeSpan.Zero)));
    }

    private static async Task<HttpResponseMessage> EnviarWebhookAsync(
        HttpClient cliente,
        string dataId,
        TimeSpan? desfase = null,
        bool firmar = true)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mercadopago")
        {
            Content = JsonContent.Create(Cuerpo(dataId)),
        };
        if (firmar)
        {
            Firmar(peticion, dataId, desfase);
        }

        return await cliente.SendAsync(peticion);
    }

    // --- T-17: approved confirma -------------------------------------------------

    [Fact]
    public async Task ApprovedVerificadoConfirmaElTurnoYRegistraElPago()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-1");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
        Assert.NotNull(turno.ConfirmadoEn);

        var pago = await contexto.Pagos.SingleAsync();
        Assert.Equal(turnoId, pago.TurnoId);
        Assert.Equal("pago-1", pago.MpPaymentId);
        Assert.Equal(Infrastructure.Pagos.ResultadoPago.Approved, pago.Resultado);
    }

    [Fact]
    public async Task WebhookSinFirmaValidaDevuelve401YSinEfectos()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-1", firmar: false);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
        Assert.Empty(await contexto.Pagos.ToListAsync());
    }

    [Fact]
    public async Task WebhookFueraDeToleranciaDevuelve401()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(
            cliente,
            "pago-1",
            desfase: -(Tolerancia + TimeSpan.FromMinutes(1)));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // --- T-18: idempotencia ------------------------------------------------------

    [Fact]
    public async Task WebhookDuplicadoDevuelve200YNoDuplicaElPago()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var primero = await EnviarWebhookAsync(cliente, "pago-1");
        var segundo = await EnviarWebhookAsync(cliente, "pago-1");

        Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);

        await using var contexto = CrearContexto();
        Assert.Single(await contexto.Pagos.ToListAsync());
    }

    [Fact]
    public async Task WebhookFueraDeOrdenDevuelve200SinEfecto()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        pasarela.ScriptearVerificacion("pago-2", ResultadoVerificacionPago.Aprobado(turnoId, "pago-2"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        await EnviarWebhookAsync(cliente, "pago-1");
        // Segundo pago distinto sobre el mismo turno ya confirmado: out-of-order.
        var respuesta = await EnviarWebhookAsync(cliente, "pago-2");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
        Assert.Single(await contexto.Pagos.ToListAsync());
    }

    [Fact]
    public async Task ApprovedTardioSobreUnTurnoExpiradoNoReviveYRegistraElPago()
    {
        var turnoId = await SembrarTurnoExpiradoAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-tarde", ResultadoVerificacionPago.Aprobado(turnoId, "pago-tarde"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-tarde");

        // 200 (no revive), pero el pago queda registrado para conciliación manual (RB-1).
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Expirado, turno.Estado);
        Assert.Equal("pago-tarde", (await contexto.Pagos.SingleAsync()).MpPaymentId);
    }

    // --- T-19: rejected y pending ------------------------------------------------

    [Fact]
    public async Task RejectedCancelaElTurnoYLibera()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-r", ResultadoVerificacionPago.Rechazado(turnoId, "pago-r"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-r");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Cancelado, turno.Estado);

        var pago = await contexto.Pagos.SingleAsync();
        Assert.Equal(Infrastructure.Pagos.ResultadoPago.Rejected, pago.Resultado);
    }

    [Fact]
    public async Task PendingRegistraPeroNoTransiciona()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-p", ResultadoVerificacionPago.Pendiente(turnoId, "pago-p"));
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-p");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);

        var pago = await contexto.Pagos.SingleAsync();
        Assert.Equal(Infrastructure.Pagos.ResultadoPago.Pending, pago.Resultado);
    }
}
