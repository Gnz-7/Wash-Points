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
/// T-31: reconciliación de canjes offline (RB-3, US-A03 CA-3.4/CA-3.5).
///
/// <c>POST /api/lavaderos/{id}/comprobantes/reconciliar</c> recibe un lote de
/// canjes registrados sin red. El servidor es la autoridad: los válidos se
/// aplican (<c>validado_offline = true</c>) y los que no reconoce no alteran el
/// turno; se informa la discrepancia.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ReconciliacionComprobanteTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly GeneradorComprobante Generador = new();
    private static readonly CifradorComprobanteAesGcm Cifrador = new(ClaveCifrado);

    private readonly ContenedorPostgres _baseDeDatos;

    public ReconciliacionComprobanteTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    private sealed record Sembrado(
        Guid LavaderoA,
        Guid TurnoA,
        string HashA,
        Guid TurnoB,
        string HashB);

    private async Task<Sembrado> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var cliente = Cliente.Crear("Cliente Reconciliación");

        var (lavaderoA, turnoA, hashA) = await SembrarTurnoConComprobanteAsync(contexto, cliente);
        var (_, turnoB, hashB) = await SembrarTurnoConComprobanteAsync(contexto, cliente);

        var hashDesconocido = new string('a', 64);
        await contexto.SaveChangesAsync();

        return new Sembrado(lavaderoA, turnoA, hashA, turnoB, hashB);
    }

    private static Task<(Guid LavaderoId, Guid TurnoId, string Hash)> SembrarTurnoConComprobanteAsync(
        WashPointsDbContext contexto, Cliente cliente)
    {
        var lavadero = Lavadero.Crear($"Lavadero {Guid.NewGuid():N}");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        var inicio = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), 3000m,
            DateTime.UtcNow.AddMinutes(5), $"clave-{Guid.NewGuid():N}");
        turno.TransicionarA(EstadoTurno.Confirmado);

        var token = Generador.GenerarToken();
        var comprobante = Comprobante.Emitir(
            turno.Id, Generador.CalcularHash(token), Cifrador.Cifrar(token), DateTime.UtcNow.AddHours(24));

        contexto.AddRange(lavadero, puesto, tipo, turno);
        contexto.Comprobantes.Add(comprobante);

        return Task.FromResult((lavadero.Id, turno.Id, comprobante.Hash));
    }

    [Fact]
    public async Task AplicaLosValidosYRechazaLosQueElServidorNoReconoce()
    {
        var sembrado = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var ahora = DateTimeOffset.UtcNow;
        var desconocido = new string('a', 64);

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{sembrado.LavaderoA}/comprobantes/reconciliar",
            new
            {
                canjes = new object[]
                {
                    new { hash = sembrado.HashA, validadoEn = ahora },
                    new { hash = desconocido, validadoEn = ahora },
                    new { hash = sembrado.HashB, validadoEn = ahora },
                },
            });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<JsonElement>();

        var aplicados = cuerpo.GetProperty("aplicados").EnumerateArray().ToList();
        Assert.Single(aplicados);
        Assert.Equal(sembrado.HashA, aplicados[0].GetProperty("hash").GetString());

        var rechazados = cuerpo.GetProperty("rechazados").EnumerateArray()
            .ToDictionary(
                r => r.GetProperty("hash").GetString()!,
                r => r.GetProperty("motivo").GetString());
        Assert.Equal(2, rechazados.Count);
        Assert.Equal("comprobante_no_encontrado", rechazados[desconocido]);
        Assert.Equal("comprobante_no_encontrado", rechazados[sembrado.HashB]);

        await using var contexto = CrearContexto();

        // CA-3.4: el canje válido se aplicó al turno correspondiente.
        var turnoA = await contexto.Turnos.SingleAsync(t => t.Id == sembrado.TurnoA);
        Assert.Equal(EstadoTurno.EnCurso, turnoA.Estado);
        var comprobanteA = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == sembrado.TurnoA);
        Assert.NotNull(comprobanteA.UsadoAt);
        Assert.True(comprobanteA.ValidadoOffline);

        // CA-3.5: lo no reconocido no altera el turno (sigue confirmado).
        var turnoB = await contexto.Turnos.SingleAsync(t => t.Id == sembrado.TurnoB);
        Assert.Equal(EstadoTurno.Confirmado, turnoB.Estado);
        var comprobanteB = await contexto.Comprobantes.SingleAsync(c => c.TurnoId == sembrado.TurnoB);
        Assert.Null(comprobanteB.UsadoAt);
    }
}
