using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// T-28: canje online del comprobante (RB-3, US-A02/US-A04).
///
/// <c>POST /api/lavaderos/{id}/comprobantes/validar</c> recibe el payload QR o
/// el código de respaldo, valida hash/vencimiento/uso/ownership/estado y aplica
/// <c>usado_at</c> + <c>confirmado → en_curso</c> en una sola transacción.
///
/// Ver SPECS/slice-02-comprobantes-rb3.md §5 (T-28) y §6 (CA-S2.2..CA-S2.10).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CanjeComprobanteTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly GeneradorComprobante Generador = new();

    private readonly ContenedorPostgres _baseDeDatos;
    private readonly CifradorComprobanteAesGcm _cifrador = new(ClaveCifrado);

    public CanjeComprobanteTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private WebApplicationFactory<Program> CrearApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.UseSetting("Comprobantes:ClaveCifrado", ClaveCifrado);
        });

    private sealed record Semilla(Guid TurnoId, Guid LavaderoId, string Qr, string Codigo);

    private async Task<Semilla> SembrarAsync(
        EstadoTurno estado = EstadoTurno.Confirmado,
        DateTime? expiraEn = null)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Canje");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Canje");

        var inicio = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), tipo.PrecioSena,
            DateTime.UtcNow.AddMinutes(5), $"clave-{Guid.NewGuid():N}");

        if (estado != EstadoTurno.PendientePago)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
            if (estado != EstadoTurno.Confirmado)
            {
                turno.TransicionarA(estado);
            }
        }

        var token = Generador.GenerarToken();
        var comprobante = Comprobante.Emitir(
            turno.Id,
            Generador.CalcularHash(token),
            _cifrador.Cifrar(token),
            expiraEn ?? DateTime.UtcNow.AddHours(24));

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        contexto.Comprobantes.Add(comprobante);
        await contexto.SaveChangesAsync();

        return new Semilla(
            turno.Id,
            lavadero.Id,
            Generador.ConstruirPayloadQr(token),
            Generador.ConstruirCodigoRespaldo(token));
    }

    private static Task<HttpResponseMessage> ValidarAsync(
        HttpClient cliente, Guid lavaderoId, string comprobante) =>
        cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/comprobantes/validar",
            new { comprobante });

    private static async Task<string?> LeerErrorAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        return cuerpo.TryGetProperty("error", out var error) ? error.GetString() : null;
    }

    [Fact]
    public async Task UnComprobanteValidoLlevaElTurnoAEnCurso()
    {
        var semilla = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.EnCurso, turno.Estado);

        var comprobante = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == semilla.TurnoId);
        Assert.NotNull(comprobante.UsadoAt);
        Assert.False(comprobante.ValidadoOffline);
    }

    [Fact]
    public async Task ElCodigoDeRespaldoDaElMismoResultado()
    {
        var semilla = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Codigo);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.EnCurso, turno.Estado);
    }

    [Fact]
    public async Task UnComprobanteYaUsadoDevuelve409SinCambiarElEstado()
    {
        var semilla = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var primero = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr);
        Assert.Equal(HttpStatusCode.OK, primero.StatusCode);

        var segundo = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr);
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("comprobante_ya_utilizado", await LeerErrorAsync(segundo));

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.EnCurso, turno.Estado);
    }

    [Fact]
    public async Task UnComprobanteDeOtroLavaderoSeRechazaSinFiltrar()
    {
        var semilla = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await ValidarAsync(cliente, Guid.NewGuid(), semilla.Qr);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
        var comprobante = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == semilla.TurnoId);
        Assert.Null(comprobante.UsadoAt);
    }

    [Fact]
    public async Task UnComprobanteVencidoDevuelve410()
    {
        var semilla = await SembrarAsync(expiraEn: DateTime.UtcNow.AddMinutes(-1));
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr);

        Assert.Equal(HttpStatusCode.Gone, respuesta.StatusCode);
        Assert.Equal("comprobante_vencido", await LeerErrorAsync(respuesta));

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
    }

    [Fact]
    public async Task UnTurnoPendienteDePagoNoPasaAEnCurso()
    {
        var semilla = await SembrarAsync(estado: EstadoTurno.PendientePago);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("pago_pendiente", await LeerErrorAsync(respuesta));

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
    }

    [Fact]
    public async Task UnTurnoYaEnCursoDevuelve409YaUtilizadoYNoNotFound()
    {
        // Reproduce la lectura no atómica del canje concurrente (CA-S2.6): el
        // perdedor puede leer el comprobante todavía sin usar (snapshot anterior
        // al commit del ganador) y el turno ya en curso. Debe responder 409
        // comprobante_ya_utilizado, nunca 404.
        var semilla = await SembrarAsync(estado: EstadoTurno.EnCurso);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("comprobante_ya_utilizado", await LeerErrorAsync(respuesta));

        await using var contexto = CrearContexto();
        var comprobante = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == semilla.TurnoId);
        Assert.Null(comprobante.UsadoAt);
    }
}
