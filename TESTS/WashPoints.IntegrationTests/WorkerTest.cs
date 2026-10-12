using Microsoft.EntityFrameworkCore;
using System.Text;
using WashPoints.Api;
using WashPoints.Application.Pagos;
using WashPoints.Domain.Catalogo;
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
/// T-21: worker de respaldo y expiración (RB-1, RB-2).
///
/// Verifica las dos reglas y su aislamiento: el respaldo a los 3 min confirma/
/// cancela consultando la pasarela; la expiración local a los 5 min libera el
/// box; y una pasarela caída no impide la expiración. Un pago tardío no revive
/// un turno ya resuelto.
///
/// Ver SPECS/slice-01-reserva-sena-mp.md T-21 y §6 (CA-S1.7, CA-S1.10).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class WorkerTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly DateTime Ahora = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly ContenedorPostgres _baseDeDatos;

    public WorkerTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private RespaldoYExpiracionServicio CrearWorker(
        WashPointsDbContext contexto,
        IPasarelaPagos pasarela) =>
        new(
            contexto,
            pasarela,
            new WebhookPagoServicio(
                pasarela, new OcupacionRepositorio(contexto), contexto, CrearEmisor(contexto),
                CrearOutbox(contexto)),
            new OpcionesPago(),
            new RelojFijo(Ahora),
            CrearOutbox(contexto));

    /// <summary>Outbox real sobre el mismo contexto, con reloj fijo (RB-2, slice-03).</summary>
    private static OutboxRepositorio CrearOutbox(WashPointsDbContext contexto) =>
        new(contexto, new RelojFijo(Ahora));

    /// <summary>
    /// Emisor con clave AES de prueba: el respaldo del worker usa el mismo
    /// camino que el webhook, que desde slice-02 también emite el comprobante
    /// (RB-3).
    /// </summary>
    private static EmisorComprobantes CrearEmisor(WashPointsDbContext contexto) =>
        new(
            new GeneradorComprobante(),
            new CifradorComprobanteAesGcm(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))),
            contexto,
            ttlVigenciaHoras: 24);

    /// <summary>
    /// Siembra un turno pendiente_pago con creado_en y expira_en controlables.
    /// `CrearParaReserva` fija creado_en en "ahora" real, por eso se ajusta por SQL.
    /// </summary>
    private async Task<Guid> SembrarPendienteAsync(
        DateTime creadoEn,
        DateTime expiraEn,
        string preferenciaId = "pref-1")
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Worker");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Worker");

        var inicio = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), tipo.PrecioSena,
            expiraEn, $"clave-{Guid.NewGuid():N}");
        turno.RegistrarPreferenciaDePago(preferenciaId);

        contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        await contexto.SaveChangesAsync();

        await _baseDeDatos.EjecutarSqlAsync(
            $"UPDATE turnos SET creado_en = '{Utc(creadoEn)}' WHERE id = '{turno.Id}'");

        return turno.Id;
    }

    private static string Utc(DateTime dt) => dt.ToString("yyyy-MM-ddTHH:mm:ssZ");

    [Fact]
    public async Task ElRespaldoApruebaUnPendienteVencidaLaVentana()
    {
        var turnoId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-4),
            expiraEn: Ahora.AddMinutes(1));
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pref-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));
        await using var contexto = CrearContexto();
        var worker = CrearWorker(contexto, pasarela);

        var resultado = await worker.EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Respaldados);

        await using var verificacion = CrearContexto();
        var turno = await verificacion.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
        Assert.Equal("pago-1", (await verificacion.Pagos.SingleAsync()).MpPaymentId);
    }

    [Fact]
    public async Task ElRespaldoCancelaUnPendienteRechazado()
    {
        var turnoId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-4),
            expiraEn: Ahora.AddMinutes(1));
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pref-1", ResultadoVerificacionPago.Rechazado(turnoId, "pago-r"));
        await using var contexto = CrearContexto();
        var worker = CrearWorker(contexto, pasarela);

        await worker.EjecutarTickAsync(CancellationToken.None);

        await using var verificacion = CrearContexto();
        var turno = await verificacion.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Cancelado, turno.Estado);
    }

    [Fact]
    public async Task ExpiraElPendienteVencidoYLiberaElPuesto()
    {
        var turnoId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-6),
            expiraEn: Ahora.AddMinutes(-1));
        var pasarela = new PasarelaPagosFakeSimulada();
        await using var contexto = CrearContexto();
        var worker = CrearWorker(contexto, pasarela);

        var resultado = await worker.EjecutarTickAsync(CancellationToken.None);

        Assert.Equal(1, resultado.Expirados);

        await using var verificacion = CrearContexto();
        var turno = await verificacion.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Expirado, turno.Estado);
        Assert.False(turno.BloqueaPuestoAhora(Ahora), "Un turno expirado no bloquea (RB-2).");
    }

    [Fact]
    public async Task UnaPasarelaCaidaNoImpideExpirarLosVencidos()
    {
        var vencidoId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-6),
            expiraEn: Ahora.AddMinutes(-1),
            preferenciaId: "pref-vencido");
        var paraRespaldarId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-4),
            expiraEn: Ahora.AddMinutes(1),
            preferenciaId: "pref-respaldo");

        var pasarela = new PasarelaPagosFakeSimulada { EnModoCaida = true };
        await using var contexto = CrearContexto();
        var worker = CrearWorker(contexto, pasarela);

        var resultado = await worker.EjecutarTickAsync(CancellationToken.None);

        // La expiración local corre igual pese a la caída de la pasarela.
        Assert.Equal(1, resultado.Expirados);
        Assert.True(resultado.Fallidos >= 1);

        await using var verificacion = CrearContexto();
        Assert.Equal(
            EstadoTurno.Expirado,
            (await verificacion.Turnos.SingleAsync(t => t.Id == vencidoId)).Estado);
        Assert.Equal(
            EstadoTurno.PendientePago,
            (await verificacion.Turnos.SingleAsync(t => t.Id == paraRespaldarId)).Estado);
    }

    [Fact]
    public async Task UnPagoAprobadoTardioNoReviveUnTurnoExpirado()
    {
        var turnoId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-6),
            expiraEn: Ahora.AddMinutes(-1));
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pref-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-tarde"));
        await using var contexto = CrearContexto();
        var worker = CrearWorker(contexto, pasarela);

        await worker.EjecutarTickAsync(CancellationToken.None);

        await using var verificacion = CrearContexto();
        var turno = await verificacion.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Expirado, turno.Estado);
        Assert.Empty(await verificacion.Pagos.ToListAsync());
    }

    [Fact]
    public async Task ElWorkerNoDuplicaUnPagoYaProcesadoPorElWebhook()
    {
        var turnoId = await SembrarPendienteAsync(
            creadoEn: Ahora.AddMinutes(-4),
            expiraEn: Ahora.AddMinutes(1));
        var pasarela = new PasarelaPagosFakeSimulada();
        pasarela.ScriptearVerificacion("pref-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));

        // El webhook procesa primero: confirma y registra el pago.
        await using (var contextoWebhook = CrearContexto())
        {
            var webhook = new WebhookPagoServicio(
                pasarela, new OcupacionRepositorio(contextoWebhook), contextoWebhook,
                CrearEmisor(contextoWebhook), CrearOutbox(contextoWebhook));
            await webhook.ProcesarAsync("payment", "pref-1", CancellationToken.None);
        }

        // El worker reejecuta el respaldo: no debe duplicar el pago ni fallar.
        await using (var contextoWorker = CrearContexto())
        {
            var worker = CrearWorker(contextoWorker, pasarela);
            await worker.EjecutarTickAsync(CancellationToken.None);
        }

        await using var verificacion = CrearContexto();
        Assert.Single(await verificacion.Pagos.ToListAsync());
        Assert.Equal(
            EstadoTurno.Confirmado,
            (await verificacion.Turnos.SingleAsync(t => t.Id == turnoId)).Estado);
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;
        public RelojFijo(DateTime ahoraUtc) => _ahora = new DateTimeOffset(ahoraUtc);
        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
