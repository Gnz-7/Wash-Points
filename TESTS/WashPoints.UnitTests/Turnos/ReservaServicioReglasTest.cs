using WashPoints.Application.Agenda;
using WashPoints.Application.Pagos;
using WashPoints.Application.ReservarTurno;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Tiempo;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.UnitTests.Turnos;

/// <summary>
/// Reglas temporales y de compatibilidad de la reserva (SPECS/slice-17 §4.7,
/// D-17.5 y D-17.6, R-2 y R-4).
///
/// El servicio valida pertenencia, ventana temporal, horario de atención y
/// compatibilidad puesto ↔ tipo <b>antes</b> de insertar el turno o pedir la
/// preferencia de pago: una reserva inválida nunca toca la pasarela (RB-1).
/// </summary>
public sealed class ReservaServicioReglasTest
{
    private static readonly Guid LavaderoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PuestoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TipoLavadoId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ClienteId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>Sábado 10/10/2026, 06:00 en Buenos Aires.</summary>
    private static readonly DateTime Ahora = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<FranjaAtencion> SabadoDeOchoAVeinte =
    [
        FranjaAtencion.Crear(
            LavaderoId,
            6,
            new TimeOnly(8, 0),
            new TimeOnly(20, 0),
            new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)),
    ];

    [Fact]
    public async Task UnaReservadentroDeLaFranjaSeAcepta()
    {
        var contexto = new Contexto();

        var turno = await contexto.Servicio.ReservarTurnoAsync(
            Solicitud(10, 0), CancellationToken.None);

        Assert.Equal(10, ConversorHorario.ALocal(turno.InicioUtc).Hour);
        Assert.Single(contexto.Ocupacion.Agregados);
        Assert.Single(contexto.Pasarela.PreferenciasCreadas);
    }

    [Fact]
    public async Task ReservarConMenosDeLaAnticipacionMinimaSeRechaza()
    {
        var contexto = new Contexto();

        await Assert.ThrowsAsync<AnticipacionInsuficienteException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(6, 2), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task ReservarEnElPasadoSeRechaza()
    {
        var contexto = new Contexto();

        await Assert.ThrowsAsync<AnticipacionInsuficienteException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(5, 30), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task ReservarMasAllaDelHorizonteSeRechaza()
    {
        var contexto = new Contexto();
        var ochoDiasDespues = ConversorHorario.AUtc(
            new DateTime(2026, 10, 18, 10, 0, 0));

        await Assert.ThrowsAsync<FueraDeHorizonteException>(
            () => contexto.Servicio.ReservarTurnoAsync(
                new SolicitudReserva(
                    LavaderoId, PuestoId, ClienteId, TipoLavadoId, ochoDiasDespues),
                CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task ReservarAntesDeAbrirSeRechaza()
    {
        var contexto = new Contexto();

        await Assert.ThrowsAsync<FueraDeHorarioAtencionException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(7, 0), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task UnTurnoQueTerminaDespuesDelCierreSeRechaza()
    {
        var contexto = new Contexto();

        // 19:45 + 30 minutos = 20:15, fuera de la franja que cierra a las 20:00.
        await Assert.ThrowsAsync<FueraDeHorarioAtencionException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(19, 45), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task ReservarUnDiaSinFranjasSeRechaza()
    {
        var contexto = new Contexto();
        var domingo = ConversorHorario.AUtc(new DateTime(2026, 10, 11, 10, 0, 0));

        await Assert.ThrowsAsync<FueraDeHorarioAtencionException>(
            () => contexto.Servicio.ReservarTurnoAsync(
                new SolicitudReserva(
                    LavaderoId, PuestoId, ClienteId, TipoLavadoId, domingo),
                CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task UnPuestoQueNoAtiendeElTipoSeRechaza()
    {
        var contexto = new Contexto(atiendeElTipo: false);

        await Assert.ThrowsAsync<PuestoIncompatibleException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(10, 0), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task UnPuestoDeOtroLavaderoSeTrataComoInexistente()
    {
        var contexto = new Contexto(puestoExiste: false);

        await Assert.ThrowsAsync<PuestoNoEncontradoException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(10, 0), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    [Fact]
    public async Task UnTipoDeOtroLavaderoSeTrataComoInexistente()
    {
        var contexto = new Contexto(tipoExiste: false);

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(
            () => contexto.Servicio.ReservarTurnoAsync(Solicitud(10, 0), CancellationToken.None));

        contexto.AssertSinEfectos();
    }

    /// <summary>Inicio a la hora local indicada del sábado 10/10/2026.</summary>
    private static SolicitudReserva Solicitud(int hora, int minuto) =>
        new(
            LavaderoId,
            PuestoId,
            ClienteId,
            TipoLavadoId,
            ConversorHorario.AUtc(new DateTime(2026, 10, 10, hora, minuto, 0)));

    /// <summary>Arma el servicio con dobles y expone lo que tocó cada puerto.</summary>
    private sealed class Contexto
    {
        public Contexto(
            bool tipoExiste = true,
            bool puestoExiste = true,
            bool atiendeElTipo = true,
            IReadOnlyList<FranjaAtencion>? franjas = null)
        {
            var catalogo = new ConsultaCatalogoFake(tipoExiste, puestoExiste, atiendeElTipo);
            var horarios = new HorarioRepositorioFake(franjas ?? SabadoDeOchoAVeinte);

            Ocupacion = new OcupacionRepositorioFake();
            Pasarela = new PasarelaPagosFakeSimulada();

            Servicio = new ReservaServicio(
                Ocupacion,
                new UnidadDeTrabajoFake(),
                catalogo,
                new OutboxFake(),
                Pasarela,
                new OpcionesPago(),
                new RelojFijo(Ahora),
                new ConversorHorarioPort(),
                horarios,
                new OpcionesAgenda());
        }

        public ReservaServicio Servicio { get; }

        public OcupacionRepositorioFake Ocupacion { get; }

        public PasarelaPagosFakeSimulada Pasarela { get; }

        public void AssertSinEfectos()
        {
            Assert.Empty(Ocupacion.Agregados);
            Assert.Empty(Pasarela.PreferenciasCreadas);
        }
    }

    private sealed class ConsultaCatalogoFake : IConsultaCatalogo
    {
        private readonly bool _tipoExiste;
        private readonly bool _puestoExiste;
        private readonly bool _atiendeElTipo;

        public ConsultaCatalogoFake(bool tipoExiste, bool puestoExiste, bool atiendeElTipo)
        {
            _tipoExiste = tipoExiste;
            _puestoExiste = puestoExiste;
            _atiendeElTipo = atiendeElTipo;
        }

        public Task<TipoLavado?> ObtenerTipoLavadoAsync(
            Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct) =>
            Task.FromResult(
                _tipoExiste
                    ? TipoLavado.Crear(lavaderoId, "Exterior", 30, 15000m, 3000m)
                    : null);

        public Task<Puesto?> ObtenerPuestoAsync(
            Guid lavaderoId, Guid puestoId, CancellationToken ct) =>
            Task.FromResult(
                _puestoExiste ? Puesto.Crear(lavaderoId, "Puesto 1") : null);

        public Task<bool> PuestoAtiendeTipoAsync(
            Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, CancellationToken ct) =>
            Task.FromResult(_atiendeElTipo);

        public Task<CatalogoLavadero?> ObtenerCatalogoAsync(
            Guid lavaderoId, CancellationToken ct) =>
            Task.FromResult<CatalogoLavadero?>(null);
    }

    private sealed class HorarioRepositorioFake : IHorarioRepositorio
    {
        private readonly IReadOnlyList<FranjaAtencion> _franjas;

        public HorarioRepositorioFake(IReadOnlyList<FranjaAtencion> franjas) =>
            _franjas = franjas;

        public Task<IReadOnlyList<FranjaAtencion>> ListarAsync(
            Guid lavaderoId, CancellationToken ct) => Task.FromResult(_franjas);

        public Task AgregarRangoAsync(
            IEnumerable<FranjaAtencion> franjas, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ReemplazarAsync(
            Guid lavaderoId,
            IReadOnlyList<FranjaAtencion> franjas,
            CancellationToken ct) => throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class OcupacionRepositorioFake : IOcupacionRepositorio
    {
        public List<WashPoints.Domain.Turnos.Turno> Agregados { get; } = new();

        public Task AgregarAsync(WashPoints.Domain.Turnos.Turno turno, CancellationToken ct)
        {
            Agregados.Add(turno);
            return Task.CompletedTask;
        }

        public Task<WashPoints.Domain.Turnos.Turno?> ObtenerAsync(
            Guid turnoId, CancellationToken ct) => throw new NotSupportedException();

        public Task<WashPoints.Domain.Turnos.Turno?> ObtenerPorIdempotencyKeyAsync(
            string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<WashPoints.Domain.Turnos.Turno>> ListarDelDiaAsync(
            Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WashPoints.Domain.Turnos.Turno>> ListarPorClienteAsync(
            Guid clienteId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TieneTurnosVigentesAsync(
            Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> TieneTurnosVigentesDelTipoAsync(
            Guid lavaderoId, Guid tipoLavadoId, DateTime ahoraUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WashPoints.Domain.Turnos.Turno>> ListarConfirmadosVencidosAsync(
            DateTime limiteUtc, CancellationToken ct) => throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class UnidadDeTrabajoFake : IUnidadDeTrabajo
    {
        public Task<T> EjecutarEnTransaccionAsync<T>(
            Func<CancellationToken, Task<T>> trabajo, CancellationToken ct) => trabajo(ct);
    }

    private sealed class OutboxFake : IOutbox
    {
        public void Encolar(EventoOcupacion evento)
        {
        }

        public void Encolar(EventoAgenda evento)
        {
        }
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTime _ahora;

        public RelojFijo(DateTime ahora) => _ahora = ahora;

        public override DateTimeOffset GetUtcNow() => new(_ahora, TimeSpan.Zero);
    }
}
