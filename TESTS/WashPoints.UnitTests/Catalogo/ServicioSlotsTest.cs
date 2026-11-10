using WashPoints.Application.Agenda;
using WashPoints.Application.Catalogo;
using WashPoints.Application.Horarios;
using WashPoints.Application.ReservarTurno;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Tiempo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// Grilla de slots de una fecha y un tipo de lavado (SPECS/slice-17 §4.3,
/// D-17.9, CA-17.18 y CA-17.19, R-1/R-2/R-4).
///
/// La grilla se arma por <c>DuracionMinutos</c> desde la apertura de cada
/// franja y descarta los slots que no entran completos antes del cierre. Un
/// slot es <c>disponible</c> sólo si queda al menos un puesto compatible libre
/// en ese intervalo y el inicio respeta la anticipación mínima; los no
/// disponibles se devuelven igual, con <c>motivo</c>, para que la UI los
/// muestre deshabilitados en vez de hacer desaparecer la grilla.
///
/// Los turnos <c>pendiente_pago</c> vigentes también ocupan puesto: es el
/// predicado <see cref="Turno.BloqueaPuestoAhora"/>, más amplio que el de
/// <c>IDisponibilidadConsulta</c>, que los excluye.
/// </summary>
public sealed class ServicioSlotsTest
{
    private static readonly Guid LavaderoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClienteId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>Sábado 10/10/2026, 06:00 en Buenos Aires.</summary>
    private static readonly DateTime Ahora = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>El sábado 10/10/2026 (ISO 6), el día que usan los casos.</summary>
    private static readonly DateOnly Sabado = new(2026, 10, 10);

    private static readonly DateTime CreadoEn = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<FranjaAtencion> SabadoDeOchoADiez =
    [
        FranjaAtencion.Crear(LavaderoId, 6, new TimeOnly(8, 0), new TimeOnly(10, 0), CreadoEn),
    ];

    [Fact]
    public async Task GeneraLaGrillaPorDuracionDesdeLaApertura()
    {
        var contexto = new Contexto();

        var grilla = await contexto.ObtenerAsync(Sabado);

        Assert.True(grilla.Abierto);
        Assert.Equal(30, grilla.DuracionMinutos);
        Assert.Equal(
            [
                new SlotCalculado(new TimeOnly(8, 0), new TimeOnly(8, 30), true, 1, null),
                new SlotCalculado(new TimeOnly(8, 30), new TimeOnly(9, 0), true, 1, null),
                new SlotCalculado(new TimeOnly(9, 0), new TimeOnly(9, 30), true, 1, null),
                new SlotCalculado(new TimeOnly(9, 30), new TimeOnly(10, 0), true, 1, null),
            ],
            grilla.Slots);
    }

    [Fact]
    public async Task DescartaElSlotQueExcedeElCierre()
    {
        var contexto = new Contexto(duracionMinutos: 45);

        var grilla = await contexto.ObtenerAsync(Sabado);

        // 08:00–08:45 y 08:45–09:30 entran; 09:30 + 45 = 10:15 excede el cierre
        // de 10:00 y se descarta (D-17.9: el remanente queda sin oferta).
        Assert.Equal(
            [new TimeOnly(8, 0), new TimeOnly(8, 45)],
            grilla.Slots.Select(slot => slot.Inicio));
    }

    [Fact]
    public async Task UneLasFranjasDelDiaEnUnaSolaGrilla()
    {
        IReadOnlyList<FranjaAtencion> franjas =
        [
            FranjaAtencion.Crear(LavaderoId, 6, new TimeOnly(8, 0), new TimeOnly(9, 0), CreadoEn),
            FranjaAtencion.Crear(LavaderoId, 6, new TimeOnly(14, 0), new TimeOnly(15, 0), CreadoEn),
        ];
        var contexto = new Contexto(franjas: franjas);

        var grilla = await contexto.ObtenerAsync(Sabado);

        Assert.Equal(
            [
                new TimeOnly(8, 0),
                new TimeOnly(8, 30),
                new TimeOnly(14, 0),
                new TimeOnly(14, 30),
            ],
            grilla.Slots.Select(slot => slot.Inicio));
        Assert.Equal(2, grilla.Franjas.Count);
    }

    [Fact]
    public async Task MarcaOcupadoCuandoTodosLosPuestosCompatiblesEstanTomados()
    {
        var contexto = new Contexto(
            cantidadPuestos: 2,
            turnos: puestos =>
            [
                TurnoConfirmado(puestos[0].Id, 8, 0, 8, 30),
                TurnoConfirmado(puestos[1].Id, 8, 0, 8, 30),
            ]);

        var grilla = await contexto.ObtenerAsync(Sabado);

        var primero = grilla.Slots[0];
        Assert.False(primero.Disponible);
        Assert.Equal(0, primero.PuestosLibres);
        Assert.Equal("ocupado", primero.Motivo);

        var segundo = grilla.Slots[1];
        Assert.True(segundo.Disponible);
        Assert.Equal(2, segundo.PuestosLibres);
        Assert.Null(segundo.Motivo);
    }

    [Fact]
    public async Task UnTurnoPendienteVigenteOcupaElPuesto()
    {
        var contexto = new Contexto(
            turnos: puestos =>
                [TurnoPendiente(puestos[0].Id, 8, 0, 8, 30, venceEn: Ahora.AddMinutes(5))]);

        var grilla = await contexto.ObtenerAsync(Sabado);

        Assert.Equal("ocupado", grilla.Slots[0].Motivo);
        Assert.Equal(0, grilla.Slots[0].PuestosLibres);
    }

    [Fact]
    public async Task UnTurnoPendienteVencidoNoOcupaElPuesto()
    {
        var contexto = new Contexto(
            turnos: puestos =>
                [TurnoPendiente(puestos[0].Id, 8, 0, 8, 30, venceEn: Ahora.AddMinutes(-1))]);

        var grilla = await contexto.ObtenerAsync(Sabado);

        Assert.True(grilla.Slots[0].Disponible);
        Assert.Equal(1, grilla.Slots[0].PuestosLibres);
    }

    [Fact]
    public async Task MarcaAnticipacionLosSlotsDentroDeLaAnticipacionMinima()
    {
        // 08:02 en Buenos Aires: el slot de 08:00 no llega a los 5 minutos.
        var contexto = new Contexto(ahora: new DateTime(2026, 10, 10, 11, 2, 0, DateTimeKind.Utc));

        var grilla = await contexto.ObtenerAsync(Sabado);

        var primero = grilla.Slots[0];
        Assert.False(primero.Disponible);
        Assert.Equal(1, primero.PuestosLibres);
        Assert.Equal("anticipacion", primero.Motivo);

        Assert.True(grilla.Slots[1].Disponible);
        Assert.Null(grilla.Slots[1].Motivo);
    }

    [Fact]
    public async Task UnPuestoIncompatibleNoCuentaComoLibre()
    {
        // Puesto 1 atiende todo; Puesto 2 sólo otro servicio y no puede tomar
        // el turno. Con el Puesto 1 ocupado, el slot queda sin oferta.
        var contexto = new Contexto(
            cantidadPuestos: 2,
            tiposPorPuesto: (puestos, otroTipo) =>
                new Dictionary<Guid, IReadOnlyList<Guid>>
                {
                    [puestos[1].Id] = [otroTipo.Id],
                },
            turnos: puestos => [TurnoConfirmado(puestos[0].Id, 8, 0, 8, 30)]);

        var grilla = await contexto.ObtenerAsync(Sabado);

        Assert.Equal(0, grilla.Slots[0].PuestosLibres);
        Assert.Equal("ocupado", grilla.Slots[0].Motivo);
        Assert.Equal(1, grilla.Slots[1].PuestosLibres);
    }

    [Fact]
    public async Task UnDiaSinFranjasDevuelveLaGrillaCerrada()
    {
        var contexto = new Contexto();

        var grilla = await contexto.ObtenerAsync(new DateOnly(2026, 10, 11));

        Assert.False(grilla.Abierto);
        Assert.Empty(grilla.Franjas);
        Assert.Empty(grilla.Slots);
    }

    [Fact]
    public async Task UnaFechaFueraDelHorizonteSeRechaza()
    {
        var contexto = new Contexto();

        await Assert.ThrowsAsync<FueraDeHorizonteException>(
            () => contexto.ObtenerAsync(Sabado.AddDays(-1)));

        await Assert.ThrowsAsync<FueraDeHorizonteException>(
            () => contexto.ObtenerAsync(Sabado.AddDays(8)));
    }

    [Fact]
    public async Task ElUltimoDiaDelHorizonteSeAcepta()
    {
        var contexto = new Contexto();

        var grilla = await contexto.ObtenerAsync(Sabado.AddDays(7));

        Assert.True(grilla.Abierto);
    }

    [Fact]
    public async Task UnLavaderoInexistenteSeRechaza()
    {
        var contexto = new Contexto(lavaderoExiste: false);

        await Assert.ThrowsAsync<LavaderoNoEncontradoException>(
            () => contexto.ObtenerAsync(Sabado));
    }

    [Fact]
    public async Task UnTipoDeOtroLavaderoSeTrataComoInexistente()
    {
        var contexto = new Contexto();

        await Assert.ThrowsAsync<TipoLavadoNoEncontradoException>(
            () => contexto.ObtenerAsync(Sabado, Guid.NewGuid()));
    }

    /// <summary>Turno confirmado en la hora local indicada del sábado.</summary>
    private static Turno TurnoConfirmado(
        Guid puestoId, int horaInicio, int minutoInicio, int horaFin, int minutoFin)
    {
        var turno = Turno.Crear(
            LavaderoId,
            puestoId,
            ClienteId,
            Guid.NewGuid(),
            Local(horaInicio, minutoInicio),
            Local(horaFin, minutoFin),
            precioSena: 3000m,
            precioTotal: 15000m);
        turno.TransicionarA(EstadoTurno.Confirmado);
        return turno;
    }

    /// <summary>Turno pendiente de pago con el vencimiento indicado.</summary>
    private static Turno TurnoPendiente(
        Guid puestoId, int horaInicio, int minutoInicio, int horaFin, int minutoFin, DateTime venceEn)
    {
        return Turno.CrearParaReserva(
            LavaderoId,
            puestoId,
            ClienteId,
            Guid.NewGuid(),
            Local(horaInicio, minutoInicio),
            Local(horaFin, minutoFin),
            precioSena: 3000m,
            precioTotal: 15000m,
            expiraEnUtc: venceEn,
            idempotencyKey: Guid.NewGuid().ToString("N"));
    }

    private static DateTime Local(int hora, int minuto) =>
        ConversorHorario.AUtc(new DateTime(2026, 10, 10, hora, minuto, 0));

    /// <summary>Arma el servicio con dobles y el escenario del caso.</summary>
    private sealed class Contexto
    {
        public Contexto(
            DateTime? ahora = null,
            int duracionMinutos = 30,
            IReadOnlyList<FranjaAtencion>? franjas = null,
            int cantidadPuestos = 1,
            Func<IReadOnlyList<Puesto>, TipoLavado, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>?
                tiposPorPuesto = null,
            Func<IReadOnlyList<Puesto>, IReadOnlyList<Turno>>? turnos = null,
            bool lavaderoExiste = true)
        {
            Puestos = Enumerable.Range(1, cantidadPuestos)
                .Select(numero => Puesto.Crear(LavaderoId, $"Puesto {numero}"))
                .ToArray();

            Tipo = TipoLavado.Crear(LavaderoId, "Lavado básico", duracionMinutos, 8000m, 1500m);
            OtroTipo = TipoLavado.Crear(LavaderoId, "Lavado completo", 60, 15000m, 2500m);

            var catalogo = lavaderoExiste
                ? new CatalogoLavadero(
                    Puestos,
                    [Tipo],
                    tiposPorPuesto?.Invoke(Puestos, OtroTipo))
                : null;

            Servicio = new ServicioSlots(
                new ConsultaCatalogoFake(catalogo),
                new HorarioRepositorioFake(franjas ?? SabadoDeOchoADiez),
                new OcupacionRepositorioFake(turnos?.Invoke(Puestos) ?? []),
                new ConversorHorarioPort(),
                new OpcionesAgenda(),
                new RelojFijo(ahora ?? Ahora));
        }

        public ServicioSlots Servicio { get; }

        public IReadOnlyList<Puesto> Puestos { get; }

        public TipoLavado Tipo { get; }

        public TipoLavado OtroTipo { get; }

        public Task<GrillaSlots> ObtenerAsync(DateOnly fecha, Guid? tipoLavadoId = null) =>
            Servicio.ObtenerAsync(
                LavaderoId, fecha, tipoLavadoId ?? Tipo.Id, CancellationToken.None);
    }

    private sealed class ConsultaCatalogoFake : IConsultaCatalogo
    {
        private readonly CatalogoLavadero? _catalogo;

        public ConsultaCatalogoFake(CatalogoLavadero? catalogo) => _catalogo = catalogo;

        public Task<TipoLavado?> ObtenerTipoLavadoAsync(
            Guid lavaderoId, Guid tipoLavadoId, CancellationToken ct) =>
            Task.FromResult(_catalogo?.Tipos.FirstOrDefault(t => t.Id == tipoLavadoId));

        public Task<Puesto?> ObtenerPuestoAsync(
            Guid lavaderoId, Guid puestoId, CancellationToken ct) =>
            Task.FromResult(_catalogo?.Puestos.FirstOrDefault(p => p.Id == puestoId));

        public Task<bool> PuestoAtiendeTipoAsync(
            Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, CancellationToken ct)
        {
            var tipos = _catalogo?.TiposDe(puestoId) ?? [];
            return Task.FromResult(tipos.Count == 0 || tipos.Contains(tipoLavadoId));
        }

        public Task<CatalogoLavadero?> ObtenerCatalogoAsync(
            Guid lavaderoId, CancellationToken ct) => Task.FromResult(_catalogo);
    }

    private sealed class HorarioRepositorioFake : IHorarioRepositorio
    {
        private readonly IReadOnlyList<FranjaAtencion> _franjas;

        public HorarioRepositorioFake(IReadOnlyList<FranjaAtencion> franjas) => _franjas = franjas;

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
        private readonly IReadOnlyList<Turno> _turnos;

        public OcupacionRepositorioFake(IReadOnlyList<Turno> turnos) => _turnos = turnos;

        public Task AgregarAsync(Turno turno, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Turno?> ObtenerPorIdempotencyKeyAsync(
            string idempotencyKey, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
            Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct) =>
            Task.FromResult(_turnos);

        public Task<IReadOnlyList<Turno>> ListarPorClienteAsync(
            Guid clienteId, CancellationToken ct) => throw new NotSupportedException();

        public Task<bool> TieneTurnosVigentesAsync(
            Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> TieneTurnosVigentesDelTipoAsync(
            Guid lavaderoId, Guid tipoLavadoId, DateTime ahoraUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(
            DateTime limiteUtc, CancellationToken ct) => throw new NotSupportedException();

        public Task GuardarAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTime _ahora;

        public RelojFijo(DateTime ahora) => _ahora = ahora;

        public override DateTimeOffset GetUtcNow() => new(_ahora, TimeSpan.Zero);
    }
}
