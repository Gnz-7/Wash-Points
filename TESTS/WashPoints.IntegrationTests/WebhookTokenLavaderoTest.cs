using Microsoft.EntityFrameworkCore;
using System.Text;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.MercadoPago;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Comprobantes;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-219 (SPECS/slice-20, D-20.5): el webhook verifica el pago con el token de
/// Mercado Pago del <b>lavadero</b>, no con un token global del operador.
///
/// - El lavadero se resuelve antes de verificar: primero por la fila
///   <c>Pago</c> que ya tenga el <c>mp_payment_id</c> (avisos duplicados o
///   tardíos) y si no existe, por el <c>user_id</c> del payload (la cuenta que
///   cobró es la del lavadero).
/// - Un 401 de la pasarela (<see cref="PasarelaTokenInvalidoException"/>) lanza
///   un refresh del par del lavadero y reintentos una vez (CA-S20.15).
/// - Un refresh fallido desvincula al lavadero y no confirma nada (RB-1).
///
/// Los tests construyen <see cref="WebhookPagoServicio"/> directamente (mismo
/// camino que el endpoint) con una pasarela falsa: nunca salen a la red.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class WebhookTokenLavaderoTest : IClassFixture<ContenedorPostgres>, IAsyncLifetime
{
    private readonly ContenedorPostgres _baseDeDatos;
    private readonly WashPointsDbContext _contexto;

    public WebhookTokenLavaderoTest(ContenedorPostgres baseDeDatos)
    {
        _baseDeDatos = baseDeDatos;
        _contexto = new WashPointsDbContext(
            new DbContextOptionsBuilder<WashPointsDbContext>()
                .UseNpgsql(baseDeDatos.CadenaConexion)
                .Options);
        LimpiarTablas();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _contexto.DisposeAsync();

    private void LimpiarTablas() =>
        _contexto.Database.ExecuteSqlRaw("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos,
                           tipos_lavado, lavaderos CASCADE;
            """);

    /// <summary>Siembra un lavadero (con <c>mp_user_id</c> si se pide) y su turno pendiente.</summary>
    private async Task<(Guid LavaderoId, Guid TurnoId)> SembrarAsync(bool conMpUserId = true)
    {
        var lavadero = Lavadero.Crear("Lavadero Webhook");
        if (conMpUserId)
        {
            // El dominio pide tokens no vacíos: aquí van los valores ya cifrados
            // que la vinculación OAuth habría persistido (los tests no los
            // descifran, la pasarela es falsa).
            lavadero.VincularMercadoPago("acceso-cifrado", "refresh-cifrado", "999", DateTimeOffset.UtcNow);
        }

        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente Webhook");

        var inicio = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            lavadero.Id, puesto.Id, cliente.Id, tipo.Id,
            inicio, inicio.AddMinutes(30), tipo.PrecioSena, tipo.PrecioTotal,
            inicio.AddHours(1), $"clave-{Guid.NewGuid():N}");
        turno.RegistrarPreferenciaDePago("pref-1");

        _contexto.AddRange(lavadero, puesto, tipo, cliente, turno);
        await _contexto.SaveChangesAsync();

        return (lavadero.Id, turno.Id);
    }

    private WebhookPagoServicio CrearServicio(PasarelaFalsa pasarela, TokensFalso tokens) =>
        new(
            pasarela,
            tokens,
            new OcupacionRepositorio(_contexto),
            _contexto,
            new EmisorComprobantes(
                new GeneradorComprobante(),
                new CifradorComprobanteAesGcm(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef"))),
                _contexto,
                ttlVigenciaHoras: 24),
            new OutboxRepositorio(_contexto, new RelojFijo()));

    [Fact]
    public async Task VerificaElPagoConElTokenDelLavaderoResueltoPorElUserIdDelPayload()
    {
        var (lavaderoId, turnoId) = await SembrarAsync();
        var pasarela = new PasarelaFalsa();
        pasarela.Scriptear(ResultadoVerificacionPago.Aprobado(turnoId, "pago-9"));
        var tokens = new TokensFalso();
        var servicio = CrearServicio(pasarela, tokens);

        var resultado = await servicio.ProcesarAsync("payment", null, "data-1", "999", CancellationToken.None);

        Assert.Equal(ResultadoWebhook.Procesado, resultado);
        Assert.Equal([lavaderoId], tokens.Obtenidos);
        Assert.Equal(["token-viejo"], pasarela.TokensUsados);

        var turno = await _contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
        var pago = await _contexto.Pagos.SingleAsync(p => p.TurnoId == turnoId);
        Assert.Equal("pago-9", pago.MpPaymentId);
    }

    [Fact]
    public async Task UnTokenRechazadoRefrescaYVerificaDeNuevo()
    {
        var (_, turnoId) = await SembrarAsync();
        var pasarela = new PasarelaFalsa();
        pasarela.Scriptear(new PasarelaTokenInvalidoException("el token fue rechazado (401)"));
        pasarela.Scriptear(ResultadoVerificacionPago.Aprobado(turnoId, "pago-9"));
        var tokens = new TokensFalso();
        var servicio = CrearServicio(pasarela, tokens);

        var resultado = await servicio.ProcesarAsync("payment", null, "data-1", "999", CancellationToken.None);

        Assert.Equal(ResultadoWebhook.Procesado, resultado);
        Assert.Equal(1, tokens.RefrescosPedidos);
        Assert.Equal("token-viejo", pasarela.TokensUsados[0]);
        Assert.Equal("token-nuevo", pasarela.TokensUsados[^1]);

        var turno = await _contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
    }

    [Fact]
    public async Task UnRefreshFallidoDesvinculaYNadaSeConfirma()
    {
        var (_, turnoId) = await SembrarAsync();
        var pasarela = new PasarelaFalsa();
        pasarela.Scriptear(new PasarelaTokenInvalidoException("el token fue rechazado (401)"));
        var tokens = new TokensFalso { LanzarNoVinculado = true };
        var servicio = CrearServicio(pasarela, tokens);

        await Assert.ThrowsAsync<MercadoPagoNoVinculadoException>(
            () => servicio.ProcesarAsync("payment", null, "data-1", "999", CancellationToken.None));

        var turno = await _contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
        Assert.Empty(await _contexto.Pagos.ToListAsync());
    }

    [Fact]
    public async Task SinLavaderoResolubleElWebhookNoHaceNada()
    {
        var (_, turnoId) = await SembrarAsync(conMpUserId: false);
        var pasarela = new PasarelaFalsa();
        var tokens = new TokensFalso();
        var servicio = CrearServicio(pasarela, tokens);

        var resultado = await servicio.ProcesarAsync("payment", null, "data-1", null, CancellationToken.None);

        Assert.Equal(ResultadoWebhook.TurnoNoEncontrado, resultado);
        Assert.Equal(0, pasarela.VerificacionesPedidas);
        var turno = await _contexto.Turnos.SingleAsync(t => t.Id == turnoId);
        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// Pasarela falsa con cola de respuestas para VerificarPagoAsync: cada
    /// llamada consume una entrada (resultado o excepción); sin cola responde
    /// Pendiente. Registra el token usado en cada llamada.
    /// </summary>
    private sealed class PasarelaFalsa : IPasarelaPagos
    {
        private readonly Queue<object> _cola = new();

        public List<string> TokensUsados { get; } = [];
        public int VerificacionesPedidas { get; private set; }

        public void Scriptear(ResultadoVerificacionPago resultado) => _cola.Enqueue(resultado);

        public void Scriptear(Exception excepcion) => _cola.Enqueue(excepcion);

        public Task<ResultadoVerificacionPago> VerificarPagoAsync(
            string pagoIdExterno, string accessToken, CancellationToken ct)
        {
            TokensUsados.Add(accessToken);
            VerificacionesPedidas++;

            if (_cola.Count == 0)
            {
                return Task.FromResult(ResultadoVerificacionPago.Pendiente());
            }

            return _cola.Dequeue() switch
            {
                Exception excepcion => throw excepcion,
                ResultadoVerificacionPago resultado => Task.FromResult(resultado),
                _ => Task.FromResult(ResultadoVerificacionPago.Pendiente()),
            };
        }

        public Task<PreferenciaCreada> CrearPreferenciaAsync(
            PreferenciaPago preferencia, string accessToken, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(
            Guid turnoId, string accessToken, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente(turnoId));

        public Task<ResultadoReembolso> ReembolsarAsync(
            string pagoId, decimal monto, string? claveIdempotencia, string accessToken, CancellationToken ct) =>
            Task.FromResult(ResultadoReembolso.Aprobado("refund-1"));

        public Task<ResultadoReembolso> ConsultarReembolsoAsync(
            string pagoId, string reembolsoId, string accessToken, CancellationToken ct) =>
            Task.FromResult(ResultadoReembolso.Aprobado("refund-1"));
    }

    /// <summary>
    /// Doble de <see cref="IObtenedorTokensMp"/>: entrega «token-viejo», y
    /// «token-nuevo» después de cada refresh (o lanza sin vínculo si se pide).
    /// </summary>
    private sealed class TokensFalso : IObtenedorTokensMp
    {
        public bool LanzarNoVinculado { get; init; }
        public List<Guid> Obtenidos { get; } = [];
        public int RefrescosPedidos { get; private set; }

        public Task<string> ObtenerTokenVigenteAsync(Guid lavaderoId, CancellationToken ct)
        {
            Obtenidos.Add(lavaderoId);
            return Task.FromResult("token-viejo");
        }

        public Task<string> RefrescarTokenAsync(Guid lavaderoId, CancellationToken ct)
        {
            RefrescosPedidos++;
            return LanzarNoVinculado
                ? throw new MercadoPagoNoVinculadoException("El refresh falló y la cuenta quedó desvinculada.")
                : Task.FromResult("token-nuevo");
        }
    }
}
