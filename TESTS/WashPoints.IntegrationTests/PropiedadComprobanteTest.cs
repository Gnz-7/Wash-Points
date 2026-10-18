using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Comprobantes;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-58: ownership del comprobante (slice-05b, CU-10 A2 / US-A09 CA-9.3).
///
/// El <c>turnoId</c> es un UUID y no es control de acceso: la entrega exige que
/// la sesión sea dueña del turno (cliente vinculado) o del lavadero. Los
/// rechazos son <c>404</c> homogéneos: no distinguen «no existe» de «no es
/// tuyo» (D-06.3), y sin sesión el pipeline devuelve <c>401</c>.
///
/// Ver SPECS/slice-05b-ownership-comprobante.md §5 y §9.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class PropiedadComprobanteTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private readonly ContenedorPostgres _baseDeDatos;

    public PropiedadComprobanteTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, usuarios, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
            builder.UseSetting("Comprobantes:ClaveCifrado", ClaveCifrado));

    /// <summary>Lavadero con un turno pendiente y su comprobante ya emitido.</summary>
    private sealed record Escenario(Guid LavaderoId, Guid ClienteId, Guid TurnoId, string TokenClaro);

    private async Task<Escenario> SembrarEscenarioAsync(string sufijo, int hora)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear($"Lavadero {sufijo}");
        var puesto = Puesto.Crear(lavadero.Id, $"Puesto {sufijo}");
        var tipo = TipoLavado.Crear(lavadero.Id, $"Exterior {sufijo}", 30, 15000m, 3000m);
        var cliente = Cliente.Crear($"Cliente {sufijo}");
        var inicio = new DateTime(2026, 10, 10, hora, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id,
            puesto.Id,
            cliente.Id,
            tipo.Id,
            inicio,
            inicio.AddMinutes(30),
            tipo.PrecioSena,
            DateTime.UtcNow.AddMinutes(5),
            $"clave-{Guid.NewGuid():N}");

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        await contexto.SaveChangesAsync();

        var tokenClaro = $"token-{sufijo}";
        contexto.Comprobantes.Add(Comprobante.Emitir(
            turno.Id,
            Guid.NewGuid().ToString("N"),
            new CifradorComprobanteAesGcm(ClaveCifrado).Cifrar(tokenClaro),
            DateTime.UtcNow.AddHours(1)));
        await contexto.SaveChangesAsync();

        return new Escenario(lavadero.Id, cliente.Id, turno.Id, tokenClaro);
    }

    private async Task<HttpResponseMessage> PedirAsync(HttpClient cliente, Guid turnoId) =>
        await cliente.GetAsync($"/api/turnos/{turnoId}/comprobante");

    [Fact]
    public async Task ClientePropietarioObtieneSuComprobante()
    {
        var escenario = await SembrarEscenarioAsync("A", 10);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(escenario.ClienteId);

        var respuesta = await PedirAsync(cliente, escenario.TurnoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<ComprobanteRespuesta>();
        Assert.Contains(escenario.TokenClaro, cuerpo!.qr);
        Assert.False(string.IsNullOrWhiteSpace(cuerpo.codigo));
    }

    [Fact]
    public async Task AdminDelLavaderoObtieneElComprobante()
    {
        var escenario = await SembrarEscenarioAsync("A", 10);
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(escenario.LavaderoId);

        var respuesta = await PedirAsync(cliente, escenario.TurnoId);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task ClienteAjenoRecibe404()
    {
        var propio = await SembrarEscenarioAsync("A", 10);
        var ajeno = await SembrarEscenarioAsync("B", 11);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(ajeno.ClienteId);

        var respuesta = await PedirAsync(cliente, propio.TurnoId);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("comprobante_no_encontrado", error!.error);
    }

    [Fact]
    public async Task ClienteSinVinculoRecibe404()
    {
        var escenario = await SembrarEscenarioAsync("A", 10);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await PedirAsync(cliente, escenario.TurnoId);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task AdminDeOtroLavaderoRecibe404()
    {
        var escenario = await SembrarEscenarioAsync("A", 10);
        var ajeno = await SembrarEscenarioAsync("B", 11);
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(ajeno.LavaderoId);

        var respuesta = await PedirAsync(cliente, escenario.TurnoId);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task SinSesionDevuelve401()
    {
        var escenario = await SembrarEscenarioAsync("A", 10);
        using var api = CrearApi();
        using var cliente = api.CrearClienteSinToken();

        var respuesta = await PedirAsync(cliente, escenario.TurnoId);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElRechazoEsIndistinguibleDeUnTurnoInexistente()
    {
        var escenario = await SembrarEscenarioAsync("A", 10);
        var ajeno = await SembrarEscenarioAsync("B", 11);
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(ajeno.ClienteId);

        var rechazado = await PedirAsync(cliente, escenario.TurnoId);
        var inexistente = await PedirAsync(cliente, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, rechazado.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(
            await rechazado.Content.ReadAsStringAsync(),
            await inexistente.Content.ReadAsStringAsync());
    }

    private sealed record ComprobanteRespuesta(string qr, string codigo, DateTime expiraEn);

    private sealed record RespuestaError(string error);
}
