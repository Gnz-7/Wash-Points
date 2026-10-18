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
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-30: caché de vigentes para el modo offline (RB-3, US-A03 CA-3.1).
///
/// <c>GET /api/lavaderos/{id}/comprobantes/vigentes?fecha=</c> devuelve solo los
/// hash de los comprobantes vigentes del día del propio lavadero: excluye
/// usados, vencidos, de otros lavaderos y de otras fechas. Nunca expone el token
/// ni el ciphertext.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ComprobanteVigentesTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly GeneradorComprobante Generador = new();
    private static readonly CifradorComprobanteAesGcm Cifrador = new(ClaveCifrado);

    private readonly ContenedorPostgres _baseDeDatos;

    public ComprobanteVigentesTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    private sealed record Sembrado(Guid LavaderoA, string HashVigente);

    private async Task<Sembrado> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var lavaderoA = Lavadero.Crear("Lavadero A");
        var puestoA = Puesto.Crear(lavaderoA.Id, "Puesto A");
        var tipoA = TipoLavado.Crear(lavaderoA.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Vigentes");

        var lavaderoB = Lavadero.Crear("Lavadero B");
        var puestoB = Puesto.Crear(lavaderoB.Id, "Puesto B");
        var tipoB = TipoLavado.Crear(lavaderoB.Id, "Interior", 30, 15000m, 3000m);

        contexto.AddRange(lavaderoA, puestoA, tipoA, lavaderoB, puestoB, tipoB, cliente);

        var dia = new DateTime(2026, 10, 10, 0, 0, 0);
        var otroDia = new DateTime(2026, 10, 11, 0, 0, 0);

        string? hashVigente = null;

        void SembrarComprobante(
            Guid lavaderoId,
            Guid puestoId,
            Guid tipoId,
            DateTime inicioLocal,
            DateTime expiraEn,
            bool usado,
            bool capturarHash)
        {
            var inicioUtc = ConversorHorario.AUtc(inicioLocal);
            var turno = Turno.CrearParaReserva(
                lavaderoId, puestoId, cliente.Id, tipoId,
                inicioUtc, inicioUtc.AddMinutes(30), 3000m,
                DateTime.UtcNow.AddMinutes(5), $"clave-{Guid.NewGuid():N}");
            turno.TransicionarA(EstadoTurno.Confirmado);

            var token = Generador.GenerarToken();
            var comprobante = Comprobante.Emitir(
                turno.Id, Generador.CalcularHash(token), Cifrador.Cifrar(token), expiraEn);
            if (usado)
            {
                comprobante.RegistrarCanje(DateTime.UtcNow);
            }

            if (capturarHash)
            {
                hashVigente = comprobante.Hash;
            }

            contexto.Add(turno);
            contexto.Comprobantes.Add(comprobante);
        }

        var futuro = DateTime.UtcNow.AddHours(24);

        // A, del día, vigente → incluido.
        SembrarComprobante(
            lavaderoA.Id, puestoA.Id, tipoA.Id, dia.AddHours(10), futuro, usado: false, capturarHash: true);
        // A, del día, ya usado → excluido.
        SembrarComprobante(
            lavaderoA.Id, puestoA.Id, tipoA.Id, dia.AddHours(11), futuro, usado: true, capturarHash: false);
        // A, del día, vencido → excluido.
        SembrarComprobante(
            lavaderoA.Id, puestoA.Id, tipoA.Id, dia.AddHours(12), DateTime.UtcNow.AddMinutes(-1), usado: false, capturarHash: false);
        // B, del día, vigente → excluido (otro lavadero).
        SembrarComprobante(
            lavaderoB.Id, puestoB.Id, tipoB.Id, dia.AddHours(13), futuro, usado: false, capturarHash: false);
        // A, otro día, vigente → excluido (otra fecha).
        SembrarComprobante(
            lavaderoA.Id, puestoA.Id, tipoA.Id, otroDia.AddHours(10), futuro, usado: false, capturarHash: false);

        await contexto.SaveChangesAsync();

        return new Sembrado(lavaderoA.Id, hashVigente!);
    }

    [Fact]
    public async Task DevuelveSoloLosHashVigentesDelDiaDelPropioLavadero()
    {
        var sembrado = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{sembrado.LavaderoA}/comprobantes/vigentes?fecha=2026-10-10");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var crudo = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cifrado", crudo, StringComparison.OrdinalIgnoreCase);

        var cuerpo = JsonDocument.Parse(crudo).RootElement;
        Assert.Equal("2026-10-10", cuerpo.GetProperty("fecha").GetString());

        var hashes = cuerpo.GetProperty("hashes").EnumerateArray()
            .Select(h => h.GetString())
            .ToList();

        Assert.Single(hashes);
        Assert.Equal(sembrado.HashVigente, hashes[0]);
    }
}
