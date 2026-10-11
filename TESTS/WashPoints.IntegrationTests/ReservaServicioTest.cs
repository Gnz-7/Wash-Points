using Microsoft.EntityFrameworkCore;
using WashPoints.Application.Pagos;
using WashPoints.Application.ReservarTurno;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-13 y T-14: la reserva con seña (RB-1).
///
/// T-13 — <c>ReservaServicio</c> crea el turno <c>pendiente_pago</c> que
/// bloquea el intervalo, pide la preferencia a la pasarela y persiste; si la
/// pasarela falla, no queda turno huérfano.
/// T-14 — un reintento idéntico devuelve el turno original, tanto si la fila
/// sigue pendiente (23P01) como si ya salió del predicado (23505), y nunca crea
/// una segunda preferencia.
///
/// Ver SPECS/slice-01-reserva-sena-mp.md §5 (T-13, T-14) y §6 (CA-S1.1, CA-S1.2,
/// CA-S1.3).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ReservaServicioTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly DateTime Ahora =
        new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly ContenedorPostgres _baseDeDatos;

    public ReservaServicioTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId, Guid ClienteId)> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id, cliente.Id);
    }

    private ReservaServicio CrearServicio(WashPointsDbContext contexto, IPasarelaPagos pasarela) =>
        new(
            new OcupacionRepositorio(contexto),
            new UnidadDeTrabajo(contexto),
            new ConsultaCatalogo(contexto),
            pasarela,
            new OpcionesPago(),
            new RelojFijo(Ahora));

    private static SolicitudReserva Solicitud(
        Guid lavaderoId, Guid puestoId, Guid tipoId, Guid clienteId, int hora = 13) =>
        new(lavaderoId, puestoId, clienteId, tipoId,
            new DateTime(2026, 10, 10, hora, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task ReservarCreaElPendienteConVencimientoYPreferencia()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        await using var contexto = CrearContexto();
        var servicio = CrearServicio(contexto, pasarela);

        var turno = await servicio.ReservarTurnoAsync(
            Solicitud(lavaderoId, puestoId, tipoId, clienteId), CancellationToken.None);

        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
        Assert.True(turno.BloqueaPuestoAhora(Ahora), "El pendiente vigente debe bloquear (RB-1).");
        Assert.Equal(Ahora.AddMinutes(5), turno.ExpiraEn);
        Assert.False(string.IsNullOrWhiteSpace(turno.MpPreferenceId));
        Assert.Equal(3000m, turno.PrecioSena);
        Assert.Single(pasarela.PreferenciasCreadas);
    }

    [Fact]
    public async Task UnaSegundaReservaDistintaDelMismoIntervaloChocaPorElExclude()
    {
        var (lavaderoId, puestoId, tipoId, _) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        await using var contexto = CrearContexto();
        var servicio = CrearServicio(contexto, pasarela);
        var cliente2 = Guid.NewGuid();

        await servicio.ReservarTurnoAsync(
            Solicitud(lavaderoId, puestoId, tipoId, Guid.NewGuid()), CancellationToken.None);

        await Assert.ThrowsAsync<TurnoConflictoException>(() =>
            servicio.ReservarTurnoAsync(
                Solicitud(lavaderoId, puestoId, tipoId, cliente2), CancellationToken.None));
    }

    [Fact]
    public async Task SiLaPasarelaFallaNoQuedaTurnoPersistido()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada { EnModoCaida = true };
        await using var contexto = CrearContexto();
        var servicio = CrearServicio(contexto, pasarela);

        await Assert.ThrowsAsync<PasarelaNoDisponibleException>(() =>
            servicio.ReservarTurnoAsync(
                Solicitud(lavaderoId, puestoId, tipoId, clienteId), CancellationToken.None));

        await using var verificacion = CrearContexto();
        Assert.Equal(0, await verificacion.Turnos.CountAsync());
    }

    [Fact]
    public async Task ReintentoIdenticoConElOriginalPendienteDevuelveElMismoTurno()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        var solicitud = Solicitud(lavaderoId, puestoId, tipoId, clienteId);

        Guid primerId;
        await using (var contexto = CrearContexto())
        {
            primerId = (await CrearServicio(contexto, pasarela)
                .ReservarTurnoAsync(solicitud, CancellationToken.None)).Id;
        }

        await using (var contexto = CrearContexto())
        {
            var segundo = await CrearServicio(contexto, pasarela)
                .ReservarTurnoAsync(solicitud, CancellationToken.None);
            Assert.Equal(primerId, segundo.Id);
        }

        await using var verificacion = CrearContexto();
        Assert.Equal(1, await verificacion.Turnos.CountAsync());
        Assert.Single(pasarela.PreferenciasCreadas);
    }

    [Fact]
    public async Task ReintentoIdenticoConElOriginalYaCanceladoDevuelveElMismoTurno()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        var solicitud = Solicitud(lavaderoId, puestoId, tipoId, clienteId);

        Guid primerId;
        await using (var contexto = CrearContexto())
        {
            primerId = (await CrearServicio(contexto, pasarela)
                .ReservarTurnoAsync(solicitud, CancellationToken.None)).Id;
        }

        // La fila original sale del predicado parcial del EXCLUDE: al reinsertar,
        // el UNIQUE de idempotency_key (23505) dispara antes que la exclusión.
        await _baseDeDatos.EjecutarSqlAsync(
            $"UPDATE turnos SET estado = 'cancelado' WHERE id = '{primerId}'");

        await using (var contexto = CrearContexto())
        {
            var segundo = await CrearServicio(contexto, pasarela)
                .ReservarTurnoAsync(solicitud, CancellationToken.None);
            Assert.Equal(primerId, segundo.Id);
        }

        await using var verificacion = CrearContexto();
        Assert.Equal(1, await verificacion.Turnos.CountAsync());
        Assert.Single(pasarela.PreferenciasCreadas);
    }

    /// <summary>Reloj controlado: fija <c>UtcNow</c> para que el TTL sea determinista.</summary>
    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;
        public RelojFijo(DateTime ahoraUtc) => _ahora = new DateTimeOffset(ahoraUtc);
        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
