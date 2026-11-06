using WashPoints.Application.Cancelacion;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Seguridad;
using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Reembolsos;

/// <summary>
/// Protege D-14.3, D-14.4 y D-14.6: la cancelación del cliente exige pertenencia,
/// respeta la máquina de estados y registra el reembolso que dicta la política.
///
/// El rechazo por pertenencia es homogéneo a propósito (turno ajeno = turno
/// inexistente) para no filtrar la existencia de turnos de otros clientes
/// (D-06.3, D-14.3).
/// </summary>
public sealed class CancelacionClienteServicioTest
{
    private static readonly Guid LavaderoId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PuestoId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ClienteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtroClienteId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid TipoLavadoId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid TurnoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTime Inicio = new(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc);

    /// <summary>Tres horas antes del turno: dentro del plazo de 120 minutos.</summary>
    private static DateTime EnPlazo => Inicio.AddMinutes(-180);

    /// <summary>Una hora antes: por debajo del plazo mínimo.</summary>
    private static DateTime FueraDePlazo => Inicio.AddMinutes(-60);

    [Fact]
    public async Task UnTurnoConfirmadoEnPlazoSeCancelaYGeneraUnReembolsoPendiente()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);

        var resultado = await escenario.Servicio.CancelarAsync(
            turno.Id, IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(EstadoTurno.Cancelado, resultado!.Turno.Estado);
        Assert.Equal(AutorCancelacion.Cliente, resultado.Turno.CanceladoPor);
        Assert.Equal(EstadoReembolso.Pendiente, resultado.Reembolso.Estado);
        Assert.Equal(MotivoReembolso.ClienteEnPlazo, resultado.Reembolso.Motivo);
        Assert.Equal(1000m, resultado.Reembolso.Monto);
        Assert.Equal(1, escenario.Turnos.Guardados);
        Assert.Single(escenario.Reembolsos.Guardados);
    }

    [Fact]
    public async Task UnTurnoPendienteDePagoSeCancelaSinReintegro()
    {
        var turno = ConTurno(EstadoTurno.PendientePago);
        var escenario = new Escenario(turno);

        var resultado = await escenario.Servicio.CancelarAsync(
            turno.Id, IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(EstadoTurno.Cancelado, resultado!.Turno.Estado);
        Assert.Equal(EstadoReembolso.NoAplica, resultado.Reembolso.Estado);
        Assert.Equal(MotivoReembolso.SinPago, resultado.Reembolso.Motivo);
        Assert.Equal(0m, resultado.Reembolso.Monto);
    }

    [Fact]
    public async Task UnTurnoFueraDePlazoSeCancelaSinReintegro()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);

        var resultado = await escenario.Servicio.CancelarAsync(
            turno.Id, IdentidadCliente(ClienteId), FueraDePlazo, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(EstadoTurno.Cancelado, resultado!.Turno.Estado);
        Assert.Equal(EstadoReembolso.NoAplica, resultado.Reembolso.Estado);
        Assert.Equal(MotivoReembolso.ClienteFueraDePlazo, resultado.Reembolso.Motivo);
        Assert.Equal(0m, resultado.Reembolso.Monto);
    }

    [Fact]
    public async Task UnTurnoDeOtroClienteSeTrataComoInexistente()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);

        var resultado = await escenario.Servicio.CancelarAsync(
            turno.Id, IdentidadCliente(OtroClienteId), EnPlazo, CancellationToken.None);

        Assert.Null(resultado);
        Assert.Equal(EstadoTurno.Confirmado, turno.Estado);
        Assert.Equal(0, escenario.Turnos.Guardados);
        Assert.Empty(escenario.Reembolsos.Guardados);
        Assert.Empty(escenario.Outbox.Eventos);
    }

    [Fact]
    public async Task UnTurnoInexistenteSeTrataComoInexistente()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);

        var resultado = await escenario.Servicio.CancelarAsync(
            Guid.NewGuid(), IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None);

        Assert.Null(resultado);
        Assert.Equal(0, escenario.Turnos.Guardados);
    }

    [Fact]
    public async Task UnaIdentidadSinClienteNoPuedeCancelar()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);
        var administrador = new IdentidadUsuario(
            Guid.NewGuid(), RolUsuario.Administrador, LavaderoId, null);

        var resultado = await escenario.Servicio.CancelarAsync(
            turno.Id, administrador, EnPlazo, CancellationToken.None);

        Assert.Null(resultado);
        Assert.Equal(0, escenario.Turnos.Guardados);
    }

    [Fact]
    public async Task UnTurnoEnCursoNoSePuedeCancelar()
    {
        var turno = ConTurno(EstadoTurno.EnCurso);
        var escenario = new Escenario(turno);

        await Assert.ThrowsAsync<TurnoTransicionInvalida>(() =>
            escenario.Servicio.CancelarAsync(
                turno.Id, IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None));

        Assert.Equal(EstadoTurno.EnCurso, turno.Estado);
        Assert.Equal(0, escenario.Turnos.Guardados);
        Assert.Empty(escenario.Reembolsos.Guardados);
    }

    [Fact]
    public async Task UnaSegundaCancelacionEsInvalidaYNoDuplicaElReembolso()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);

        await escenario.Servicio.CancelarAsync(
            turno.Id, IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None);

        await Assert.ThrowsAsync<TurnoTransicionInvalida>(() =>
            escenario.Servicio.CancelarAsync(
                turno.Id, IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None));

        Assert.Single(escenario.Reembolsos.Guardados);
        Assert.Equal(1, escenario.Turnos.Guardados);
    }

    [Fact]
    public async Task LaCancelacionEncolaLosEventosDeOcupacionYDeAgenda()
    {
        var turno = ConTurno(EstadoTurno.Confirmado);
        var escenario = new Escenario(turno);

        await escenario.Servicio.CancelarAsync(
            turno.Id, IdentidadCliente(ClienteId), EnPlazo, CancellationToken.None);

        Assert.Equal(2, escenario.Outbox.Eventos.Count);
        Assert.Contains(escenario.Outbox.Eventos, evento => evento is EventoOcupacion);
        Assert.Contains(escenario.Outbox.Eventos, evento => evento is EventoAgenda);
    }

    private static IdentidadUsuario IdentidadCliente(Guid clienteId) =>
        new(Guid.NewGuid(), RolUsuario.Cliente, null, clienteId);

    private static Turno ConTurno(EstadoTurno estado, decimal precioSena = 1000m)
    {
        var turno = Turno.Crear(
            lavaderoId: LavaderoId,
            puestoId: PuestoId,
            clienteId: ClienteId,
            tipoLavadoId: TipoLavadoId,
            inicioUtc: Inicio,
            finUtc: Inicio.AddMinutes(30),
            precioSena: precioSena,
            precioTotal: 15000m);

        foreach (var paso in CaminoHasta(estado))
        {
            turno.TransicionarA(paso);
        }

        return turno;
    }

    private static IEnumerable<EstadoTurno> CaminoHasta(EstadoTurno destino) => destino switch
    {
        EstadoTurno.PendientePago => [],
        EstadoTurno.Confirmado => [EstadoTurno.Confirmado],
        EstadoTurno.EnCurso => [EstadoTurno.Confirmado, EstadoTurno.EnCurso],
        _ => throw new ArgumentOutOfRangeException(nameof(destino), destino, "Estado no usado en estos tests."),
    };

    /// <summary>Escenario con los tres dobles y el servicio ya cableado.</summary>
    private sealed class Escenario
    {
        public Escenario(Turno turno)
        {
            Turnos = new OcupacionRepositorioFalso(turno);
            Reembolsos = new ReembolsoRepositorioFalso();
            Outbox = new OutboxFalso();
            Servicio = new CancelacionClienteServicio(
                Turnos,
                Reembolsos,
                new PoliticaReembolso(new OpcionesReembolso()),
                Outbox);
        }

        public OcupacionRepositorioFalso Turnos { get; }

        public ReembolsoRepositorioFalso Reembolsos { get; }

        public OutboxFalso Outbox { get; }

        public CancelacionClienteServicio Servicio { get; }
    }

    private sealed class OcupacionRepositorioFalso(Turno turno) : IOcupacionRepositorio
    {
        public int Guardados { get; private set; }

        public Task AgregarAsync(Turno nuevo, CancellationToken ct) => Task.CompletedTask;

        public Task<Turno?> ObtenerAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult<Turno?>(turnoId == turno.Id ? turno : null);

        public Task<Turno?> ObtenerPorIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
            Task.FromResult<Turno?>(null);

        public Task<IReadOnlyList<Turno>> ListarDelDiaAsync(
            Guid lavaderoId, DateTime desdeUtc, DateTime hastaUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Turno>>([]);

        public Task<IReadOnlyList<Turno>> ListarPorClienteAsync(
            Guid clienteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Turno>>([]);

        public Task<bool> TieneTurnosVigentesAsync(
            Guid lavaderoId, Guid puestoId, DateTime ahoraUtc, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> TieneTurnosVigentesDelTipoAsync(
            Guid lavaderoId, Guid tipoLavadoId, DateTime ahoraUtc, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<Turno>> ListarConfirmadosVencidosAsync(
            DateTime limiteUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Turno>>([]);

        public Task GuardarAsync(CancellationToken ct)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    private sealed class ReembolsoRepositorioFalso : IReembolsoRepositorio
    {
        public List<Reembolso> Guardados { get; } = [];

        public Task AgregarAsync(Reembolso reembolso, CancellationToken ct)
        {
            Guardados.Add(reembolso);
            return Task.CompletedTask;
        }

        public Task<Reembolso?> ObtenerPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult(Guardados.Find(reembolso => reembolso.TurnoId == turnoId));
    }

    private sealed class OutboxFalso : IOutbox
    {
        public List<object> Eventos { get; } = [];

        public void Encolar(EventoOcupacion evento) => Eventos.Add(evento);

        public void Encolar(EventoAgenda evento) => Eventos.Add(evento);
    }
}
