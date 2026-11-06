using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Turnos;

/// <summary>
/// Cancelación iniciada por el cliente (slice-14, D-14.3 y D-14.4).
///
/// La máquina de estados sigue siendo la única puerta: un turno en curso o ya
/// cerrado no se puede cancelar, y la autoría queda registrada para que el
/// reembolso y las métricas puedan distinguir quién canceló.
/// </summary>
public sealed class CancelacionTurnoTest
{
    /// <summary>Estados desde los que la tabla de ARCHITECTURE §5 permite cancelar.</summary>
    public static TheoryData<EstadoTurno> EstadosCancelables =>
        new()
        {
            EstadoTurno.PendientePago,
            EstadoTurno.Confirmado,
        };

    /// <summary>Estados en los que cancelar ya no es posible.</summary>
    public static TheoryData<EstadoTurno> EstadosNoCancelables =>
        new()
        {
            EstadoTurno.EnCurso,
            EstadoTurno.Finalizado,
            EstadoTurno.Cancelado,
            EstadoTurno.NoPresentado,
            EstadoTurno.Expirado,
        };

    [Fact]
    public void UnTurnoQueNoSeCanceloNoTieneAutorDeCancelacion()
    {
        var turno = ConEstado(EstadoTurno.Confirmado);

        Assert.Null(turno.CanceladoPor);
    }

    [Theory]
    [MemberData(nameof(EstadosCancelables))]
    public void ElClientePuedeCancelarYQuedaRegistradaLaAutoria(EstadoTurno desde)
    {
        var turno = ConEstado(desde);

        turno.CancelarPorCliente();

        Assert.Equal(EstadoTurno.Cancelado, turno.Estado);
        Assert.Equal(AutorCancelacion.Cliente, turno.CanceladoPor);
    }

    [Theory]
    [MemberData(nameof(EstadosCancelables))]
    public void ElAdministradorPuedeCancelarYQuedaRegistradaLaAutoria(EstadoTurno desde)
    {
        var turno = ConEstado(desde);

        turno.CancelarPorAdministrador();

        Assert.Equal(EstadoTurno.Cancelado, turno.Estado);
        Assert.Equal(AutorCancelacion.Administrador, turno.CanceladoPor);
    }

    [Theory]
    [MemberData(nameof(EstadosNoCancelables))]
    public void DesdeUnEstadoNoCancelableLaTransicionEsInvalida(EstadoTurno desde)
    {
        var turno = ConEstado(desde);

        Assert.Throws<TurnoTransicionInvalida>(() => turno.CancelarPorCliente());
        Assert.Equal(desde, turno.Estado);
        Assert.Null(turno.CanceladoPor);
    }

    [Fact]
    public void CancelarCierraElTurnoConLaHoraDelCierre()
    {
        var turno = ConEstado(EstadoTurno.Confirmado);

        turno.CancelarPorCliente();

        Assert.NotNull(turno.CerradoEn);
    }

    [Fact]
    public void CancelarPorAdministradorNoPisaLaAutoriaDelCliente()
    {
        var turno = ConEstado(EstadoTurno.Confirmado);
        turno.CancelarPorCliente();

        Assert.Throws<TurnoTransicionInvalida>(() => turno.CancelarPorAdministrador());
        Assert.Equal(AutorCancelacion.Cliente, turno.CanceladoPor);
    }

    private static Turno ConEstado(EstadoTurno destino)
    {
        var turno = Turno.Crear(
            lavaderoId: Guid.NewGuid(),
            puestoId: Guid.NewGuid(),
            clienteId: Guid.NewGuid(),
            tipoLavadoId: Guid.NewGuid(),
            inicioUtc: new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            finUtc: new DateTime(2026, 10, 10, 13, 30, 0, DateTimeKind.Utc),
            precioSena: 1000m,
            precioTotal: 15000m);

        foreach (var paso in CaminoHasta(destino))
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
        EstadoTurno.Finalizado => [EstadoTurno.Confirmado, EstadoTurno.EnCurso, EstadoTurno.Finalizado],
        EstadoTurno.Cancelado => [EstadoTurno.Confirmado, EstadoTurno.Cancelado],
        EstadoTurno.NoPresentado => [EstadoTurno.Confirmado, EstadoTurno.NoPresentado],
        EstadoTurno.Expirado => [EstadoTurno.Expirado],
        _ => throw new ArgumentOutOfRangeException(nameof(destino), destino, "Estado sin camino definido."),
    };
}
