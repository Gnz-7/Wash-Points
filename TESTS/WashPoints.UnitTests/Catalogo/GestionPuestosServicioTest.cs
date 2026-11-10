using WashPoints.Application.Catalogo;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// Compatibilidad puesto ↔ tipo de lavado (SPECS/slice-17 §4.5, T-181, R-4).
///
/// El servicio valida pertenencia antes de escribir: el puesto tiene que ser
/// del lavadero del path y cada tipo tiene que existir, estar activo y ser del
/// mismo lavadero. La lista vacía es válida y deja el puesto atendiendo todos
/// los servicios (D-17.7). El conjunto se reemplaza completo, sin duplicados.
/// </summary>
public sealed class GestionPuestosServicioTest
{
    private static readonly Guid LavaderoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PuestoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TipoBasicoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TipoCompletoId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TipoAjenoId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task AsignaLosTiposCompatiblesDelLavadero()
    {
        var puestos = new PuestoRepositorioFake();
        var servicio = Crear(puestos: puestos);

        var tipos = await servicio.AsignarTiposAsync(
            LavaderoId, PuestoId, new[] { TipoBasicoId, TipoCompletoId }, CancellationToken.None);

        Assert.Equal(new[] { TipoBasicoId, TipoCompletoId }, tipos);
        Assert.Equal(1, puestos.Guardados);
        var reemplazo = Assert.Single(puestos.Reemplazos);
        Assert.Equal(PuestoId, reemplazo.PuestoId);
        Assert.Equal(new[] { TipoBasicoId, TipoCompletoId }, reemplazo.Tipos);
    }

    [Fact]
    public async Task UnaListaVaciaDejaElPuestoAtendiendoTodosLosServicios()
    {
        var puestos = new PuestoRepositorioFake();
        var servicio = Crear(puestos: puestos);

        var tipos = await servicio.AsignarTiposAsync(
            LavaderoId, PuestoId, Array.Empty<Guid>(), CancellationToken.None);

        Assert.Empty(tipos);
        var reemplazo = Assert.Single(puestos.Reemplazos);
        Assert.Empty(reemplazo.Tipos);
    }

    [Fact]
    public async Task UnPuestoDeOtroLavaderoNoSeConfigura()
    {
        var puestos = new PuestoRepositorioFake { ExistePuesto = false };
        var servicio = Crear(puestos: puestos);

        await Assert.ThrowsAsync<PuestoNoEncontradoException>(
            () => servicio.AsignarTiposAsync(
                LavaderoId, PuestoId, new[] { TipoBasicoId }, CancellationToken.None));

        Assert.Empty(puestos.Reemplazos);
        Assert.Equal(0, puestos.Guardados);
    }

    [Fact]
    public async Task UnTipoDeOtroLavaderoSeRechaza()
    {
        var puestos = new PuestoRepositorioFake();
        var catalogo = new ConsultaCatalogoFake
        {
            TiposDelLavadero = { TipoBasicoId },
        };
        var servicio = Crear(puestos: puestos, catalogo: catalogo);

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(
            () => servicio.AsignarTiposAsync(
                LavaderoId, PuestoId, new[] { TipoBasicoId, TipoAjenoId }, CancellationToken.None));

        Assert.Empty(puestos.Reemplazos);
        Assert.Equal(0, puestos.Guardados);
    }

    [Fact]
    public async Task DeduplicaLosTiposRepetidos()
    {
        var puestos = new PuestoRepositorioFake();
        var servicio = Crear(puestos: puestos);

        var tipos = await servicio.AsignarTiposAsync(
            LavaderoId, PuestoId, new[] { TipoBasicoId, TipoBasicoId, TipoCompletoId }, CancellationToken.None);

        Assert.Equal(new[] { TipoBasicoId, TipoCompletoId }, tipos);
        var reemplazo = Assert.Single(puestos.Reemplazos);
        Assert.Equal(2, reemplazo.Tipos.Count);
    }

    private static GestionPuestosServicio Crear(
        PuestoRepositorioFake? puestos = null,
        ConsultaCatalogoFake? catalogo = null) =>
        new(
            puestos ?? new PuestoRepositorioFake(),
            new OcupacionRepositorioFake(),
            catalogo ?? new ConsultaCatalogoFake { TiposDelLavadero = { TipoBasicoId, TipoCompletoId } },
            TimeProvider.System);

    private sealed class PuestoRepositorioFake : IPuestoRepositorio
    {
        public bool ExistePuesto { get; init; } = true;

        public List<(Guid PuestoId, IReadOnlyList<Guid> Tipos)> Reemplazos { get; } = new();

        public int Guardados { get; private set; }

        public Task AgregarAsync(Puesto puesto, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Puesto?> ObtenerAsync(
            Guid lavaderoId, Guid puestoId, CancellationToken ct = default) =>
            Task.FromResult(
                ExistePuesto && lavaderoId == LavaderoId && puestoId == PuestoId
                    ? Puesto.Crear(lavaderoId, "Puesto 1")
                    : null);

        public Task<IReadOnlyList<Puesto>> ListarAsync(
            Guid lavaderoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ObtenerTiposAsync(
            Guid puestoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> ObtenerTiposPorPuestoAsync(
            Guid lavaderoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ReemplazarTiposAsync(
            Guid puestoId, IReadOnlyList<Guid> tiposLavadoIds, CancellationToken ct = default)
        {
            Reemplazos.Add((puestoId, tiposLavadoIds));
            return Task.CompletedTask;
        }

        public Task GuardarAsync(CancellationToken ct = default)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    private sealed class ConsultaCatalogoFake : IConsultaCatalogo
    {
        public HashSet<Guid> TiposDelLavadero { get; } = new();

        public Task<TipoLavado?> ObtenerTipoLavadoAsync(
            Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct = default) =>
            Task.FromResult(
                lavaderoId == LavaderoId && TiposDelLavadero.Contains(tipoLavadoId)
                    ? TipoLavado.Crear(lavaderoId, "Lavado", 30, 8000m, 1500m)
                    : null);

        public Task<Puesto?> ObtenerPuestoAsync(
            Guid lavaderoId, Guid puestoId, CancellationToken ct = default) =>
            Task.FromResult<Puesto?>(null);

        public Task<bool> PuestoAtiendeTipoAsync(
            Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<CatalogoLavadero?> ObtenerCatalogoAsync(
            Guid lavaderoId, CancellationToken ct = default) =>
            Task.FromResult<CatalogoLavadero?>(
                new CatalogoLavadero(Array.Empty<Puesto>(), Array.Empty<TipoLavado>()));
    }

    private sealed class OcupacionRepositorioFake : IOcupacionRepositorio
    {
        public Task AgregarAsync(Turno turno, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Turno?> ObtenerPorIdempotencyKeyAsync(
            string idempotencyKey, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
            Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarPorClienteAsync(
            Guid clienteId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> TieneTurnosVigentesAsync(
            Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> TieneTurnosVigentesDelTipoAsync(
            Guid lavaderoId, Guid tipoLavadoId, DateTime ahoraUtc, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(
            DateTime limiteUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
