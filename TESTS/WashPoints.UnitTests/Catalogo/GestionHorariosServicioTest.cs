using WashPoints.Application.Catalogo;
using WashPoints.Application.Horarios;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// ABM de horarios del panel (SPECS/slice-17 §4.1/§4.2, T-178, R-1).
///
/// El servicio es la única puerta de escritura de la semana: valida forma,
/// orden y superposición, reemplaza el conjunto completo dentro de una
/// transacción y trata un lavadero inexistente como 404.
/// </summary>
public sealed class GestionHorariosServicioTest
{
    private static readonly Guid LavaderoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task DevuelveLasFranjasDelLavadero()
    {
        var repo = new HorarioRepositorioFake
        {
            Existentes =
            {
                Franja(1, "08:00", "13:00"),
                Franja(1, "14:00", "20:00"),
            },
        };
        var servicio = Crear(repo: repo);

        var franjas = await servicio.ObtenerAsync(LavaderoId);

        Assert.Equal(2, franjas.Count);
        Assert.All(franjas, f => Assert.Equal(LavaderoId, f.LavaderoId));
    }

    [Fact]
    public async Task UnLavaderoInexistenteNoSeConfigura()
    {
        var servicio = Crear(catalogo: new ConsultaCatalogoFake(existe: false));

        await Assert.ThrowsAsync<LavaderoNoEncontradoException>(
            () => servicio.ObtenerAsync(LavaderoId));
        await Assert.ThrowsAsync<LavaderoNoEncontradoException>(
            () => servicio.ReemplazarAsync(LavaderoId, new[] { Solicitada(1, "08:00", "20:00") }));
    }

    [Fact]
    public async Task ReemplazaLaSemanaCompletaDentroDeUnaTransaccion()
    {
        var repo = new HorarioRepositorioFake();
        var unidad = new UnidadDeTrabajoFake();
        var servicio = Crear(repo: repo, unidad: unidad);

        var franjas = await servicio.ReemplazarAsync(
            LavaderoId,
            new[]
            {
                Solicitada(2, "14:00", "20:00"),
                Solicitada(1, "08:00", "13:00"),
            });

        Assert.Equal(2, franjas.Count);
        Assert.Equal(new[] { 1, 2 }, franjas.Select(f => f.DiaSemana).ToArray());
        Assert.Equal(1, unidad.Transacciones);
        Assert.Equal(2, repo.Reemplazadas.Count);
    }

    [Fact]
    public async Task UnaListaVaciaDejaElLavaderoCerrado()
    {
        var repo = new HorarioRepositorioFake();
        var servicio = Crear(repo: repo);

        var franjas = await servicio.ReemplazarAsync(
            LavaderoId, Array.Empty<FranjaSolicitada>());

        Assert.Empty(franjas);
        Assert.Empty(repo.Reemplazadas);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task UnDiaFueraDeRangoEsFranjaInvalida(int dia)
    {
        var servicio = Crear();

        var error = await Assert.ThrowsAsync<FranjaInvalidaException>(
            () => servicio.ReemplazarAsync(
                LavaderoId, new[] { Solicitada(dia, "08:00", "20:00") }));

        Assert.Contains("no es válido", error.Message);
    }

    [Fact]
    public async Task LaAperturaDebeSerAnteriorAlCierre()
    {
        var servicio = Crear();

        var error = await Assert.ThrowsAsync<FranjaInvalidaException>(
            () => servicio.ReemplazarAsync(
                LavaderoId, new[] { Solicitada(1, "20:00", "08:00") }));

        Assert.Contains("anterior al cierre", error.Message);
    }

    [Fact]
    public async Task LasFranjasSuperpuestasDelMismoDiaSeRechazan()
    {
        var servicio = Crear();

        var error = await Assert.ThrowsAsync<FranjaInvalidaException>(
            () => servicio.ReemplazarAsync(
                LavaderoId,
                new[]
                {
                    Solicitada(1, "08:00", "13:00"),
                    Solicitada(1, "12:00", "20:00"),
                }));

        Assert.Contains("se superponen", error.Message);
    }

    [Fact]
    public async Task MasDeSeisFranjasPorDiaSeRechaza()
    {
        var servicio = Crear();
        var franjas = Enumerable.Range(0, 7)
            .Select(i => Solicitada(1, $"{8 + i * 2:00}:00", $"{9 + i * 2:00}:00"))
            .ToArray();

        var error = await Assert.ThrowsAsync<FranjaInvalidaException>(
            () => servicio.ReemplazarAsync(LavaderoId, franjas));

        Assert.Contains("máximo", error.Message);
    }

    [Theory]
    [InlineData("8:00")]
    [InlineData("ocho")]
    [InlineData("")]
    public async Task UnaHoraMalFormadaSeRechaza(string hora)
    {
        var servicio = Crear();

        await Assert.ThrowsAsync<FranjaInvalidaException>(
            () => servicio.ReemplazarAsync(
                LavaderoId, new[] { Solicitada(1, hora, "20:00") }));
    }

    private static FranjaSolicitada Solicitada(int dia, string apertura, string cierre) =>
        new(dia, apertura, cierre);

    private static FranjaAtencion Franja(int dia, string apertura, string cierre) =>
        FranjaAtencion.Crear(
            LavaderoId,
            dia,
            TimeOnly.Parse(apertura),
            TimeOnly.Parse(cierre),
            new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc));

    private static GestionHorariosServicio Crear(
        HorarioRepositorioFake? repo = null,
        ConsultaCatalogoFake? catalogo = null,
        UnidadDeTrabajoFake? unidad = null) =>
        new(
            repo ?? new HorarioRepositorioFake(),
            catalogo ?? new ConsultaCatalogoFake(existe: true),
            unidad ?? new UnidadDeTrabajoFake(),
            TimeProvider.System);

    private sealed class HorarioRepositorioFake : IHorarioRepositorio
    {
        public List<FranjaAtencion> Existentes { get; } = new();

        public List<FranjaAtencion> Reemplazadas { get; } = new();

        public Task<IReadOnlyList<FranjaAtencion>> ListarAsync(
            Guid lavaderoId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FranjaAtencion>>(
                Existentes.Where(f => f.LavaderoId == lavaderoId).ToArray());

        public Task AgregarRangoAsync(
            IEnumerable<FranjaAtencion> franjas, CancellationToken ct = default)
        {
            Existentes.AddRange(franjas);
            return Task.CompletedTask;
        }

        public Task ReemplazarAsync(
            Guid lavaderoId,
            IReadOnlyList<FranjaAtencion> franjas,
            CancellationToken ct = default)
        {
            Reemplazadas.AddRange(franjas);
            return Task.CompletedTask;
        }

        public Task GuardarAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ConsultaCatalogoFake : IConsultaCatalogo
    {
        private readonly bool _existe;

        public ConsultaCatalogoFake(bool existe) => _existe = existe;

        public Task<TipoLavado?> ObtenerTipoLavadoAsync(
            Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct = default) =>
            Task.FromResult<TipoLavado?>(null);

        public Task<Puesto?> ObtenerPuestoAsync(
            Guid lavaderoId, Guid puestoId, CancellationToken ct = default) =>
            Task.FromResult<Puesto?>(null);

        public Task<bool> PuestoAtiendeTipoAsync(
            Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<CatalogoLavadero?> ObtenerCatalogoAsync(
            Guid lavaderoId, CancellationToken ct = default) =>
            Task.FromResult(
                _existe
                    ? new CatalogoLavadero(
                        Array.Empty<Puesto>(), Array.Empty<TipoLavado>())
                    : null);
    }

    private sealed class UnidadDeTrabajoFake : IUnidadDeTrabajo
    {
        public int Transacciones { get; private set; }

        public async Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> trabajo, CancellationToken ct)
        {
            Transacciones++;
            return await trabajo(ct);
        }
    }
}
