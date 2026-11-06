using WashPoints.Application.Catalogo;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// Gestión del catálogo de tipos de lavado (SPECS/slice-13, T-131).
///
/// Cubre las decisiones D-13.1 (catálogo por lavadero), D-13.3 (baja lógica),
/// D-13.4 (no se desactiva un tipo con turnos vigentes) y D-13.9 (un tipo de
/// otro lavadero responde como inexistente). El reloj se inyecta para fijar el
/// instante que se pasa al predicado de vigencia.
/// </summary>
public sealed class GestionTiposLavadoServicioTest
{
    private static readonly Guid LavaderoId = Guid.NewGuid();
    private static readonly DateTime AhoraUtc = new(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CrearAgregaYGuarda()
    {
        var (servicio, tipos, _, _) = Crear();

        var tipo = await servicio.CrearAsync(LavaderoId, "Lavado básico", 30, 8000m, 1500m, default);

        Assert.Single(tipos.Agregados);
        Assert.Equal(1, tipos.Guardadas);
        Assert.Equal(LavaderoId, tipo.LavaderoId);
        Assert.Equal("Lavado básico", tipo.Nombre);
        Assert.True(tipo.Activo);
    }

    [Fact]
    public async Task CrearConSenaEnCeroNoLlegaAPersistir()
    {
        var (servicio, tipos, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            servicio.CrearAsync(LavaderoId, "Lavado básico", 30, 8000m, 0m, default));

        Assert.Empty(tipos.Agregados);
        Assert.Equal(0, tipos.Guardadas);
    }

    [Fact]
    public async Task CrearPropagaElNombreDuplicado()
    {
        var (servicio, tipos, _, _) = Crear();
        tipos.ErrorAlGuardar = new TipoLavadoDuplicadoException("Ya existe ese tipo de lavado.");

        await Assert.ThrowsAsync<TipoLavadoDuplicadoException>(() =>
            servicio.CrearAsync(LavaderoId, "Lavado básico", 30, 8000m, 1500m, default));
    }

    [Fact]
    public async Task ListarDelegaEnElRepositorio()
    {
        var (servicio, tipos, _, _) = Crear();
        tipos.AgregarExistente(TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m));

        var listado = await servicio.ListarAsync(LavaderoId, default);

        Assert.Single(listado);
    }

    [Fact]
    public async Task EditarCambiaLosDatosYGuarda()
    {
        var (servicio, tipos, _, _) = Crear();
        var tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m);
        tipos.AgregarExistente(tipo);

        var editado = await servicio.EditarAsync(
            LavaderoId, tipo.Id, "Lavado completo", 60, 15000m, 2500m, default);

        Assert.Equal("Lavado completo", editado.Nombre);
        Assert.Equal(60, editado.DuracionMinutos);
        Assert.Equal(15000m, editado.PrecioTotal);
        Assert.Equal(2500m, editado.PrecioSena);
        Assert.Equal(1, tipos.Guardadas);
    }

    [Fact]
    public async Task EditarUnTipoInexistenteLanzaNoEncontrado()
    {
        var (servicio, _, _, _) = Crear();

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(() =>
            servicio.EditarAsync(LavaderoId, Guid.NewGuid(), "Lavado", 30, 8000m, 1500m, default));
    }

    [Fact]
    public async Task EditarUnTipoDeOtroLavaderoLanzaNoEncontrado()
    {
        var (servicio, tipos, _, _) = Crear();
        var ajeno = TipoLavado.Crear(Guid.NewGuid(), "Lavado básico", 30, 8000m, 1500m);
        tipos.AgregarExistente(ajeno);

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(() =>
            servicio.EditarAsync(LavaderoId, ajeno.Id, "Lavado completo", 60, 15000m, 2500m, default));
    }

    [Fact]
    public async Task EditarConDatosInvalidosNoGuarda()
    {
        var (servicio, tipos, _, _) = Crear();
        var tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m);
        tipos.AgregarExistente(tipo);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            servicio.EditarAsync(LavaderoId, tipo.Id, "Lavado básico", 30, 8000m, 9000m, default));

        Assert.Equal(0, tipos.Guardadas);
        Assert.Equal("Lavado básico", tipo.Nombre);
    }

    [Fact]
    public async Task DesactivarConTurnosVigentesLanzaConflicto()
    {
        var (servicio, tipos, ocupacion, _) = Crear();
        var tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m);
        tipos.AgregarExistente(tipo);
        ocupacion.HayVigentes = true;

        await Assert.ThrowsAsync<TipoLavadoConTurnosActivosException>(() =>
            servicio.DesactivarAsync(LavaderoId, tipo.Id, default));

        Assert.True(tipo.Activo);
        Assert.Equal(0, tipos.Guardadas);
    }

    [Fact]
    public async Task DesactivarSinTurnosVigentesLoDejaInactivo()
    {
        var (servicio, tipos, ocupacion, _) = Crear();
        var tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m);
        tipos.AgregarExistente(tipo);
        ocupacion.HayVigentes = false;

        var desactivado = await servicio.DesactivarAsync(LavaderoId, tipo.Id, default);

        Assert.False(desactivado.Activo);
        Assert.Equal(1, tipos.Guardadas);
    }

    [Fact]
    public async Task DesactivarConsultaLaVigenciaConElRelojYElTipoCorrectos()
    {
        var (servicio, tipos, ocupacion, _) = Crear();
        var tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m);
        tipos.AgregarExistente(tipo);

        await servicio.DesactivarAsync(LavaderoId, tipo.Id, default);

        Assert.Equal(tipo.Id, ocupacion.UltimoTipoId);
        Assert.Equal(AhoraUtc, ocupacion.UltimoAhoraUtc);
    }

    [Fact]
    public async Task DesactivarUnTipoInexistenteLanzaNoEncontradoAntesDeConsultarVigencia()
    {
        var (servicio, _, ocupacion, _) = Crear();

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(() =>
            servicio.DesactivarAsync(LavaderoId, Guid.NewGuid(), default));

        Assert.Null(ocupacion.UltimoTipoId);
    }

    [Fact]
    public async Task ActivarLoReactiva()
    {
        var (servicio, tipos, _, _) = Crear();
        var tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", 30, 8000m, 1500m);
        tipo.Desactivar();
        tipos.AgregarExistente(tipo);

        var reactivado = await servicio.ActivarAsync(LavaderoId, tipo.Id, default);

        Assert.True(reactivado.Activo);
        Assert.Equal(1, tipos.Guardadas);
    }

    [Fact]
    public async Task ActivarUnTipoInexistenteLanzaNoEncontrado()
    {
        var (servicio, _, _, _) = Crear();

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(() =>
            servicio.ActivarAsync(LavaderoId, Guid.NewGuid(), default));
    }

    private static (
        GestionTiposLavadoServicio Servicio,
        TipoLavadoRepositorioFake Tipos,
        OcupacionRepositorioFake Ocupacion,
        RelojFijo Reloj) Crear()
    {
        var tipos = new TipoLavadoRepositorioFake();
        var ocupacion = new OcupacionRepositorioFake();
        var reloj = new RelojFijo(AhoraUtc);

        return (new GestionTiposLavadoServicio(tipos, ocupacion, reloj), tipos, ocupacion, reloj);
    }

    private sealed class TipoLavadoRepositorioFake : ITipoLavadoRepositorio
    {
        private readonly Dictionary<Guid, TipoLavado> _existentes = new();

        public List<TipoLavado> Agregados { get; } = new();

        public int Guardadas { get; private set; }

        public Exception? ErrorAlGuardar { get; set; }

        public void AgregarExistente(TipoLavado tipo) => _existentes[tipo.Id] = tipo;

        public Task AgregarAsync(TipoLavado tipo, CancellationToken ct)
        {
            Agregados.Add(tipo);
            return Task.CompletedTask;
        }

        public Task<TipoLavado?> ObtenerAsync(Guid lavaderoId, Guid tipoId, CancellationToken ct) =>
            Task.FromResult(
                _existentes.TryGetValue(tipoId, out var tipo) && tipo.LavaderoId == lavaderoId
                    ? tipo
                    : null);

        public Task<IReadOnlyList<TipoLavado>> ListarAsync(Guid lavaderoId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TipoLavado>>(
                _existentes.Values.Where(t => t.LavaderoId == lavaderoId).ToArray());

        public Task GuardarAsync(CancellationToken ct)
        {
            if (ErrorAlGuardar is not null)
            {
                return Task.FromException(ErrorAlGuardar);
            }

            Guardadas++;
            return Task.CompletedTask;
        }
    }

    private sealed class OcupacionRepositorioFake : IOcupacionRepositorio
    {
        public bool HayVigentes { get; set; }

        public Guid? UltimoTipoId { get; private set; }

        public DateTime? UltimoAhoraUtc { get; private set; }

        public Task<bool> TieneTurnosVigentesDelTipoAsync(
            Guid lavaderoId,
            Guid tipoLavadoId,
            DateTime ahoraUtc,
            CancellationToken ct)
        {
            UltimoTipoId = tipoLavadoId;
            UltimoAhoraUtc = ahoraUtc;
            return Task.FromResult(HayVigentes);
        }

        public Task AgregarAsync(Turno turno, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
            Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarPorClienteAsync(
            Guid clienteId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> TieneTurnosVigentesAsync(
            Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(
            DateTime limiteUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;

        public RelojFijo(DateTime ahoraUtc) => _ahora = new DateTimeOffset(ahoraUtc);

        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
