using System.Net;
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
/// CA-S11.12: la caché offline de vigentes no puede incluir un turno sin seña
/// acreditada (RB-1).
///
/// El invariante hoy se sostiene solo de rebote: el comprobante se emite
/// exclusivamente desde el webhook de pago aprobado, así que un
/// <c>pendiente_pago</c> no tiene comprobante que cachear. Este test siembra el
/// caso que ese argumento no cubre —un turno <c>pendiente_pago</c> que <b>sí</b>
/// tiene comprobante— para que la suite falle si alguien mueve la emisión a la
/// reserva. Es una alarma de regresión, no una descripción del flujo actual:
/// un test que siembra el turno sin comprobante pasaría aunque la regla se
/// rompa.
///
/// El modo offline valida contra esta lista sin red, de modo que un hash de más
/// aquí habilita la atención sin pago. De ahí que se excluya por estado del
/// turno y no solo por <c>UsadoAt</c>/<c>ExpiraEn</c> del comprobante.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ComprobanteVigentesRb1Test : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly string ClaveCifrado =
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"));

    private static readonly GeneradorComprobante Generador = new();
    private static readonly CifradorComprobanteAesGcm Cifrador = new(ClaveCifrado);

    private readonly ContenedorPostgres _baseDeDatos;

    public ComprobanteVigentesRb1Test(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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
        Guid LavaderoId,
        string HashConfirmado,
        string HashPendientePago,
        string HashCancelado);

    private async Task<Sembrado> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero RB-1");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto RB-1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente RB-1");

        contexto.AddRange(lavadero, puesto, tipo, cliente);

        var dia = new DateTime(2026, 10, 10, 0, 0, 0);
        var expiraComprobante = DateTime.UtcNow.AddHours(24);

        string Sembrar(DateTime inicioLocal, EstadoTurno estado, bool pasarPorConfirmado)
        {
            var inicioUtc = ConversorHorario.AUtc(inicioLocal);
            var turno = Turno.CrearParaReserva(
                lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
                inicioUtc, inicioUtc.AddMinutes(30), 3000m, 15000m,
                DateTime.UtcNow.AddMinutes(5), $"clave-{Guid.NewGuid():N}");

            if (pasarPorConfirmado)
            {
                turno.TransicionarA(EstadoTurno.Confirmado);
            }

            if (estado != turno.Estado)
            {
                turno.TransicionarA(estado);
            }

            var token = Generador.GenerarToken();
            var comprobante = Comprobante.Emitir(
                turno.Id, Generador.CalcularHash(token), Cifrador.Cifrar(token), expiraComprobante);

            contexto.Add(turno);
            contexto.Comprobantes.Add(comprobante);

            return comprobante.Hash;
        }

        // Con seña acreditada: es el único que puede habilitar la atención.
        var hashConfirmado = Sembrar(dia.AddHours(10), EstadoTurno.Confirmado, pasarPorConfirmado: true);

        // Sin seña acreditada, pero con comprobante: la regresión que se vigila.
        var hashPendientePago = Sembrar(dia.AddHours(11), EstadoTurno.PendientePago, pasarPorConfirmado: false);

        // Pagado y luego cancelado: el comprobante ya no habilita nada.
        var hashCancelado = Sembrar(dia.AddHours(12), EstadoTurno.Cancelado, pasarPorConfirmado: true);

        await contexto.SaveChangesAsync();

        return new Sembrado(lavadero.Id, hashConfirmado, hashPendientePago, hashCancelado);
    }

    [Fact]
    public async Task UnTurnoPendienteDePagoNoApareceAunqueTengaComprobante()
    {
        var sembrado = await SembrarAsync();

        // El sembrado tiene que haber persistido los comprobantes "de más":
        // si no, el test pasaría sin haber puesto a prueba la regla.
        await using (var contexto = CrearContexto())
        {
            Assert.True(await contexto.Comprobantes.AnyAsync(c => c.Hash == sembrado.HashPendientePago));
            Assert.True(await contexto.Comprobantes.AnyAsync(c => c.Hash == sembrado.HashCancelado));
        }

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{sembrado.LavaderoId}/comprobantes/vigentes?fecha=2026-10-10");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;
        var hashes = cuerpo.GetProperty("hashes").EnumerateArray()
            .Select(h => h.GetString())
            .ToList();

        Assert.DoesNotContain(sembrado.HashPendientePago, hashes);
        Assert.DoesNotContain(sembrado.HashCancelado, hashes);
        Assert.Equal(new[] { sembrado.HashConfirmado }, hashes);
    }
}
