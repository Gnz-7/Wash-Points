using System.Net;
using System.Net.Http.Json;
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
/// T-29: canje concurrente — criterio de cierre del slice-02 (RB-3, US-A04 CA-4.2).
///
/// Dos canjes simultáneos del mismo comprobante contra PostgreSQL real: el gate
/// es un único <c>UPDATE ... WHERE usado_at IS NULL</c>. Exactamente uno gana
/// (<c>200</c>); el otro recibe <c>409 comprobante_ya_utilizado</c>. Debe haber
/// un solo <c>usado_at</c> y una sola transición <c>confirmado → en_curso</c>.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CanjeConcurrenteTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly GeneradorComprobante Generador = new();

    private readonly ContenedorPostgres _baseDeDatos;

    public CanjeConcurrenteTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.UseSetting("Comprobantes:ClaveCifrado", ClaveCifrado);
        });

    private async Task<(Guid TurnoId, Guid LavaderoId, string Qr)> SembrarConfirmadoAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Concurrencia");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Concurrencia");

        var inicio = new DateTime(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), tipo.PrecioSena,
            DateTime.UtcNow.AddMinutes(5), $"clave-{Guid.NewGuid():N}");
        turno.TransicionarA(EstadoTurno.Confirmado);

        var token = Generador.GenerarToken();
        var comprobante = Comprobante.Emitir(
            turno.Id,
            Generador.CalcularHash(token),
            new CifradorComprobanteAesGcm(ClaveCifrado).Cifrar(token),
            DateTime.UtcNow.AddHours(24));

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        contexto.Comprobantes.Add(comprobante);
        await contexto.SaveChangesAsync();

        return (turno.Id, lavadero.Id, Generador.ConstruirPayloadQr(token));
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
    public async Task DosCanjesConcurrentesUnoGanaYElOtroDevuelve409()
    {
        var semilla = await SembrarConfirmadoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuestas = await Task.WhenAll(
            ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr),
            ValidarAsync(cliente, semilla.LavaderoId, semilla.Qr));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var perdedor = respuestas.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("comprobante_ya_utilizado", await LeerErrorAsync(perdedor));

        await using var contexto = CrearContexto();
        var turno = await contexto.Turnos.SingleAsync(t => t.Id == semilla.TurnoId);
        Assert.Equal(EstadoTurno.EnCurso, turno.Estado);

        var comprobante = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == semilla.TurnoId);
        Assert.NotNull(comprobante.UsadoAt);
        Assert.Equal(1, await contexto.Comprobantes.CountAsync(c => c.UsadoAt != null));
    }
}
