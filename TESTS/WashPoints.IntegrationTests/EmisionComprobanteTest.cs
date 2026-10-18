using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Comprobantes;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-26: emisión del comprobante dentro de la confirmación de la seña
/// (RB-3, US-C08 CA-8.1).
///
/// El comprobante viaja en el MISMO commit que `pendiente_pago → confirmado`:
/// un webhook aprobado deja exactamente un comprobante con hash y copia cifrada;
/// un duplicado no lo reemite; y sin clave de cifrado la emisión falla de forma
/// explícita sin confirmar el turno.
///
/// Ver SPECS/slice-02-comprobantes-rb3.md §5 (T-26) y §6 (CA-S2.1).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class EmisionComprobanteTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string Secreto = "secreto-de-prueba-webhook";
    private static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private readonly ContenedorPostgres _baseDeDatos;

    public EmisionComprobanteTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

        var lavadero = Lavadero.Crear("Lavadero Comprobantes");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Comprobantes");

        var inicio = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), tipo.PrecioSena,
            DateTime.UtcNow.AddMinutes(5), $"clave-{Guid.NewGuid():N}");

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        await contexto.SaveChangesAsync();

        return turno.Id;
    }

    private CustomWebApplicationFactory CrearApi(
        PasarelaPagosFakeSimulada pasarela,
        string? claveCifrado)
    {
        var factory = new CustomWebApplicationFactory(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.UseSetting("Pagos:WebhookSecret", Secreto);
            if (claveCifrado is not null)
            {
                builder.UseSetting("Comprobantes:ClaveCifrado", claveCifrado);
            }

            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(pasarela);
            });
        });

        return factory;
    }

    private static string Firmar(string dataId, string requestId, DateTimeOffset instante)
    {
        var ts = instante.ToUnixTimeSeconds();
        var manifiesto = $"id:{dataId};request-id:{requestId};ts:{ts};";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(Secreto));
        var hash = Convert
            .ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifiesto)))
            .ToLowerInvariant();
        return $"ts={ts},v1={hash}";
    }

    private static async Task<HttpResponseMessage> EnviarWebhookAsync(HttpClient cliente, string dataId)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mercadopago")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                type = "payment",
                action = "payment.created",
                data = new { id = dataId },
            }),
        };
        peticion.Headers.Add("x-request-id", requestId);
        peticion.Headers.Add("x-signature", Firmar(dataId, requestId, DateTimeOffset.UtcNow));

        return await cliente.SendAsync(peticion);
    }

    [Fact]
    public async Task AprobarLaSenaEmiteExactamenteUnComprobante()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela, ClaveCifrado);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-1");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);

        var comprobante = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == turnoId);
        Assert.Equal(64, comprobante.Hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", comprobante.Hash);
        Assert.False(string.IsNullOrWhiteSpace(comprobante.TokenCifrado));
        Assert.False(comprobante.Usado);
        Assert.False(comprobante.ValidadoOffline);
        Assert.Null(comprobante.UsadoAt);

        // Expira a las 24 h de la confirmación (D2).
        var vencimientoEsperado = turno.ConfirmadoEn!.Value.AddHours(24);
        Assert.True(
            (comprobante.ExpiraEn - vencimientoEsperado).Duration() < TimeSpan.FromMinutes(1),
            $"expira_en={comprobante.ExpiraEn:o} no coincidió con confirmadoEn+24h={vencimientoEsperado:o}.");
    }

    [Fact]
    public async Task UnWebhookDuplicadoNoReemiteElComprobante()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela, ClaveCifrado);
        using var cliente = api.CreateClient();

        await EnviarWebhookAsync(cliente, "pago-1");
        var segundo = await EnviarWebhookAsync(cliente, "pago-1");

        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);

        await using var contexto = CrearContexto();
        Assert.Single(await contexto.Comprobantes.Where(c => c.TurnoId == turnoId).ToListAsync());
    }

    [Fact]
    public async Task SinClaveDeCifradoLaEmisionFallaSinConfirmarElTurno()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela, claveCifrado: null);
        using var cliente = api.CreateClient();

        var respuesta = await EnviarWebhookAsync(cliente, "pago-1");

        // Falla explícita (CU-04): no hay confirmado sin comprobante (RB-3).
        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
        Assert.Empty(await contexto.Comprobantes.ToListAsync());
    }

    // --- T-27: entrega al cliente -------------------------------------------------

    [Fact]
    public async Task ElComprobanteSeRecuperaDesdeElServidorYElClaroNuncaSeGuarda()
    {
        const string prefijoQr = "washpoints://comprobante/";
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        using var api = CrearApi(pasarela, ClaveCifrado);
        using var cliente = api.CreateClient();

        await EnviarWebhookAsync(cliente, "pago-1");

        // US-C08 CA-8.4: el comprobante se recupera sin depender de push.
        var respuesta = await cliente.GetAsync($"/api/turnos/{turnoId}/comprobante");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var qr = cuerpo.GetProperty("qr").GetString()!;
        var codigo = cuerpo.GetProperty("codigo").GetString()!;

        Assert.StartsWith(prefijoQr, qr);
        var tokenDesdeQr = qr[prefijoQr.Length..];

        // CA-S2.3: QR y código decodifican al mismo token canónico.
        var generador = new GeneradorComprobante();
        Assert.Equal(tokenDesdeQr, generador.ExtraerTokenDePayloadQr(qr));
        Assert.Equal(tokenDesdeQr, generador.DecodificarCodigoRespaldo(codigo));

        // CA-S2.8: la base nunca guarda el claro; solo hash + ciphertext.
        await using var contexto = CrearContexto();
        var comprobante = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == turnoId);
        Assert.DoesNotContain(tokenDesdeQr, comprobante.Hash);
        Assert.DoesNotContain(tokenDesdeQr, comprobante.TokenCifrado);
        Assert.NotEqual(tokenDesdeQr, comprobante.TokenCifrado);
    }

    [Fact]
    public async Task UnTurnoPendienteNoTieneComprobanteRecuperable()
    {
        var turnoId = await SembrarTurnoPendienteAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        using var api = CrearApi(pasarela, ClaveCifrado);
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync($"/api/turnos/{turnoId}/comprobante");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
