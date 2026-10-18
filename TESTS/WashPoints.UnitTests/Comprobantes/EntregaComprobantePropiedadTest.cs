using WashPoints.Application.Comprobantes;
using WashPoints.Domain.Comprobantes;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Seguridad;
using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Comprobantes;

/// <summary>
/// T-57: regla de propiedad del comprobante (slice-05b, D-06.3).
///
/// La entrega solo procede si la identidad es dueña del turno: el cliente
/// vinculado (<c>clienteId == turno.clienteId</c>) o el administrador del
/// lavadero (<c>lavaderoId == turno.lavaderoId</c>). Cualquier otro caso
/// devuelve <c>null</c>, que el endpoint traduce a un <c>404</c> homogéneo.
/// </summary>
public sealed class EntregaComprobantePropiedadTest
{
    private static readonly Guid LavaderoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtroLavaderoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClienteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtroClienteId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PuestoId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TipoLavadoId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private const string TokenClaro = "token-de-prueba";

    [Fact]
    public async Task ElClienteDuenoObtieneSuComprobante()
    {
        var servicio = CrearServicio();
        var identidad = new IdentidadUsuario(Guid.NewGuid(), RolUsuario.Cliente, null, ClienteId);

        var entrega = await servicio.ObtenerAsync(TurnoId, identidad, CancellationToken.None);

        Assert.NotNull(entrega);
        Assert.Contains(TokenClaro, entrega!.PayloadQr);
        Assert.False(string.IsNullOrWhiteSpace(entrega.CodigoRespaldo));
    }

    [Fact]
    public async Task ElClienteAjenoNoObtieneElComprobante()
    {
        var servicio = CrearServicio();
        var identidad = new IdentidadUsuario(Guid.NewGuid(), RolUsuario.Cliente, null, OtroClienteId);

        var entrega = await servicio.ObtenerAsync(TurnoId, identidad, CancellationToken.None);

        Assert.Null(entrega);
    }

    [Fact]
    public async Task ElClienteSinVinculoNoObtieneElComprobante()
    {
        var servicio = CrearServicio();
        var identidad = new IdentidadUsuario(Guid.NewGuid(), RolUsuario.Cliente, null, null);

        var entrega = await servicio.ObtenerAsync(TurnoId, identidad, CancellationToken.None);

        Assert.Null(entrega);
    }

    [Fact]
    public async Task ElAdministradorDelLavaderoObtieneElComprobante()
    {
        var servicio = CrearServicio();
        var identidad = new IdentidadUsuario(Guid.NewGuid(), RolUsuario.Administrador, LavaderoId, null);

        var entrega = await servicio.ObtenerAsync(TurnoId, identidad, CancellationToken.None);

        Assert.NotNull(entrega);
    }

    [Fact]
    public async Task ElAdministradorDeOtroLavaderoNoObtieneElComprobante()
    {
        var servicio = CrearServicio();
        var identidad = new IdentidadUsuario(Guid.NewGuid(), RolUsuario.Administrador, OtroLavaderoId, null);

        var entrega = await servicio.ObtenerAsync(TurnoId, identidad, CancellationToken.None);

        Assert.Null(entrega);
    }

    [Fact]
    public async Task SinIdentidadNoEntregaElComprobante()
    {
        var servicio = CrearServicio();

        var entrega = await servicio.ObtenerAsync(TurnoId, null, CancellationToken.None);

        Assert.Null(entrega);
    }

    private static Guid TurnoId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static EntregaComprobanteServicio CrearServicio()
    {
        var inicio = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var turno = Turno.CrearParaReserva(
            LavaderoId,
            PuestoId,
            ClienteId,
            TipoLavadoId,
            inicio,
            inicio.AddMinutes(30),
            3000m,
            DateTime.UtcNow.AddMinutes(5),
            "clave-de-prueba");

        var comprobante = Comprobante.Emitir(
            turno.Id,
            "hash-de-prueba",
            "token-cifrado",
            DateTime.UtcNow.AddHours(1));

        return new EntregaComprobanteServicio(
            new TurnosFake(turno),
            new ComprobantesFake(comprobante),
            new CifradorFake(),
            new GeneradorFake());
    }

    private sealed class TurnosFake : IOcupacionRepositorio
    {
        private readonly Turno _turno;

        public TurnosFake(Turno turno) => _turno = turno;

        public Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult<Turno?>(_turno);

        public Task AgregarAsync(Turno turno, CancellationToken ct) => throw new NotSupportedException();
        public Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Turno>> ListarDelDiaAsync(Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TieneTurnosVigentesAsync(Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(DateTime limiteUtc, CancellationToken ct) => throw new NotSupportedException();
        public Task GuardarAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ComprobantesFake : IComprobanteRepositorio
    {
        private readonly Comprobante _comprobante;

        public ComprobantesFake(Comprobante comprobante) => _comprobante = comprobante;

        public Task<Comprobante?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult<Comprobante?>(_comprobante);

        public Task<Comprobante?> ObtenerPorHashAsync(string hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> MarcarCanjeadoSiDisponibleAsync(Guid comprobanteId, DateTime usadoEnUtc, bool offline, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListarHashesVigentesAsync(IReadOnlyCollection<Guid> turnoIds, DateTime ahoraUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CifradorFake : ICifradorComprobante
    {
        public string Cifrar(string tokenClaro) => tokenClaro;

        public string Descifrar(string tokenCifrado) => TokenClaro;
    }

    private sealed class GeneradorFake : IGeneradorComprobante
    {
        public string GenerarToken() => TokenClaro;

        public string CalcularHash(string token) => "hash";

        public string ConstruirPayloadQr(string token) => IGeneradorComprobante.PrefijoPayloadQr + token;

        public string ExtraerTokenDePayloadQr(string payload) => payload;

        public string ConstruirCodigoRespaldo(string token) => "CODIGO-DE-RESPALDO";

        public string DecodificarCodigoRespaldo(string codigo) => TokenClaro;
    }
}
