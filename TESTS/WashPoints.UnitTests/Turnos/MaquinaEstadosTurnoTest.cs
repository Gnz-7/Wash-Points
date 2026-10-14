using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Turnos;

/// <summary>
/// Matriz de transiciones de la máquina de estados de `Turno`.
///
/// Es el test de regresión de RB-1: si alguien habilita un atajo que lleve un
/// turno sin seña a ocupar un puesto, o que revierta un turno a "sin pagar",
/// este test falla.
///
/// Fuente: DOCS/ARCHITECTURE.md §5 (tabla de transiciones) y
/// SPECS/slice-00-nucleo-turnos.md T-03.
/// </summary>
public sealed class MaquinaEstadosTurnoTest
{
    public static TheoryData<EstadoTurno, EstadoTurno> TransicionesValidas => new()
    {
        { EstadoTurno.PendientePago, EstadoTurno.Confirmado },
        { EstadoTurno.PendientePago, EstadoTurno.Cancelado },
        { EstadoTurno.PendientePago, EstadoTurno.Expirado },
        { EstadoTurno.Confirmado, EstadoTurno.EnCurso },
        { EstadoTurno.Confirmado, EstadoTurno.NoPresentado },
        { EstadoTurno.Confirmado, EstadoTurno.Cancelado },
        { EstadoTurno.EnCurso, EstadoTurno.Finalizado },
    };

    public static TheoryData<EstadoTurno, EstadoTurno> TransicionesInvalidas => new()
    {
        // Toda llegada desde un estado terminal es ilegal.
        { EstadoTurno.Finalizado, EstadoTurno.Confirmado },
        { EstadoTurno.Cancelado, EstadoTurno.Confirmado },
        { EstadoTurno.NoPresentado, EstadoTurno.Confirmado },
        { EstadoTurno.Finalizado, EstadoTurno.EnCurso },
        { EstadoTurno.Cancelado, EstadoTurno.EnCurso },
        { EstadoTurno.NoPresentado, EstadoTurno.EnCurso },

        // RB-1: el pago tardío NO revive un turno vencido (SPECS/slice-01 §3).
        { EstadoTurno.Expirado, EstadoTurno.Confirmado },
        { EstadoTurno.Expirado, EstadoTurno.EnCurso },
        { EstadoTurno.Expirado, EstadoTurno.Cancelado },

        // RB-1: nada sin pagar puede ocupar un puesto.
        { EstadoTurno.PendientePago, EstadoTurno.EnCurso },
        { EstadoTurno.PendientePago, EstadoTurno.Finalizado },
        { EstadoTurno.PendientePago, EstadoTurno.NoPresentado },

        // Un turno pagado nunca vuelve a "sin pagar".
        { EstadoTurno.Confirmado, EstadoTurno.PendientePago },
        { EstadoTurno.EnCurso, EstadoTurno.PendientePago },

        // No se puede saltar de pago directo a atención sin validar el QR.
        { EstadoTurno.Confirmado, EstadoTurno.Finalizado },

        // Una vez iniciada la atención, no se cancela: se finaliza.
        { EstadoTurno.EnCurso, EstadoTurno.Cancelado },
    };

    [Fact]
    public void AlCrearElTurnoNaceEnPendientePago()
    {
        var turno = Turno.Crear(
            lavaderoId: Guid.NewGuid(),
            puestoId: Guid.NewGuid(),
            clienteId: Guid.NewGuid(),
            tipoLavadoId: Guid.NewGuid(),
            inicioUtc: new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            finUtc: new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            precioSena: 1000m);

        Assert.Equal(EstadoTurno.PendientePago, turno.Estado);
    }

    [Theory]
    [MemberData(nameof(TransicionesValidas))]
    public void TransicionesPermitidasPorLaTablaDeArquitectura(EstadoTurno desde, EstadoTurno hacia)
    {
        var turno = ConEstado(desde);

        turno.TransicionarA(hacia);

        Assert.Equal(hacia, turno.Estado);
    }

    [Theory]
    [MemberData(nameof(TransicionesInvalidas))]
    public void TransicionesRechazadas(EstadoTurno desde, EstadoTurno hacia)
    {
        var turno = ConEstado(desde);

        var excepcion = Assert.Throws<TurnoTransicionInvalida>(
            () => turno.TransicionarA(hacia));

        Assert.Contains(desde.ToString(), excepcion.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(hacia.ToString(), excepcion.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesdeEstadosTerminalesNoSePuedeConfirmar()
    {
        // Test ancla de RB-1 (T-03): un turno ya cerrado o cancelado no puede
        // pasar a ocupar un puesto. Es el caso de la tabla de trazabilidad de
        // REQUIREMENTS/US/US-C03 CA-3.1.
        foreach (var terminal in new[]
        {
            EstadoTurno.Finalizado,
            EstadoTurno.Cancelado,
            EstadoTurno.NoPresentado,
            EstadoTurno.Expirado,
        })
        {
            var turno = ConEstado(terminal);

            Assert.Throws<TurnoTransicionInvalida>(
                () => turno.TransicionarA(EstadoTurno.Confirmado));
        }
    }

    [Theory]
    [InlineData(EstadoTurno.PendientePago, false)]
    [InlineData(EstadoTurno.Confirmado, true)]
    [InlineData(EstadoTurno.EnCurso, true)]
    [InlineData(EstadoTurno.Finalizado, false)]
    [InlineData(EstadoTurno.Cancelado, false)]
    [InlineData(EstadoTurno.NoPresentado, false)]
    [InlineData(EstadoTurno.Expirado, false)]
    public void OcupaPuestoCoincideConElPredicadoDelExclude(EstadoTurno estado, bool ocupa)
    {
        // OcupaPuesto es el espejo en código de
        //   WHERE (estado IN ('confirmado', 'en_curso'))
        // de ARCHITECTURE §4.1. Si diverge, el EXCLUDE y el dominio deciden
        // distinto sobre la misma fila y RB-1 se rompe en silencio.
        var turno = ConEstado(estado);

        Assert.Equal(ocupa, turno.OcupaPuesto);
    }

    [Theory]
    [InlineData(EstadoTurno.PendientePago, true)]   // vigente: bloquea (decisión slice-01)
    [InlineData(EstadoTurno.Confirmado, true)]
    [InlineData(EstadoTurno.EnCurso, true)]
    [InlineData(EstadoTurno.Finalizado, false)]
    [InlineData(EstadoTurno.Cancelado, false)]
    [InlineData(EstadoTurno.NoPresentado, false)]
    [InlineData(EstadoTurno.Expirado, false)]
    public void BloqueaPuestoAhoraEsOcupaPuestoMasElPendienteVigente(EstadoTurno estado, bool bloquea)
    {
        // Dual de OcupaPuesto (SPECS/slice-01 §3): la disponibilidad usa
        // `BloqueaPuestoAhora(now)`, que suma el pendiente_pago aún vigente.
        // El EXCLUDE, en cambio, usa los 3 estados. Si ambos divergen, la
        // disponibilidad y la base deciden distinto sobre la misma fila (RB-2).
        var ahora = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var turno = ConEstado(estado);

        if (estado == EstadoTurno.PendientePago)
        {
            turno.FijarVencimiento(ahora.AddMinutes(5));
        }

        Assert.Equal(bloquea, turno.BloqueaPuestoAhora(ahora));
    }

    [Fact]
    public void ElPendienteVencidoDejaDeBloquear()
    {
        var ahora = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var turno = Turno.Crear(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ahora, ahora.AddHours(1), precioSena: 1000m);
        turno.FijarVencimiento(ahora.AddMinutes(5));

        Assert.True(turno.BloqueaPuestoAhora(ahora));
        Assert.False(turno.BloqueaPuestoAhora(ahora.AddMinutes(5)));
    }

    /// <summary>Cambia el estado por la única vía permitida, o falla explícitamente.</summary>
    private static Turno ConEstado(EstadoTurno destino)
    {
        var turno = Turno.Crear(
            lavaderoId: Guid.NewGuid(),
            puestoId: Guid.NewGuid(),
            clienteId: Guid.NewGuid(),
            tipoLavadoId: Guid.NewGuid(),
            inicioUtc: new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            finUtc: new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            precioSena: 1000m);

        foreach (var paso in CaminoHasta(destino))
        {
            turno.TransicionarA(paso);
        }

        Assert.Equal(destino, turno.Estado);
        return turno;
    }

    /// <summary>Camino único desde `pendiente_pago` hasta cada estado alcanzable.</summary>
    private static IEnumerable<EstadoTurno> CaminoHasta(EstadoTurno destino) => destino switch
    {
        EstadoTurno.PendientePago => [],
        EstadoTurno.Confirmado => [EstadoTurno.Confirmado],
        EstadoTurno.Cancelado => [EstadoTurno.Cancelado],
        EstadoTurno.EnCurso => [EstadoTurno.Confirmado, EstadoTurno.EnCurso],
        EstadoTurno.Finalizado => [EstadoTurno.Confirmado, EstadoTurno.EnCurso, EstadoTurno.Finalizado],
        EstadoTurno.NoPresentado => [EstadoTurno.Confirmado, EstadoTurno.NoPresentado],
        EstadoTurno.Expirado => [EstadoTurno.Expirado],
        _ => throw new ArgumentOutOfRangeException(nameof(destino), destino, "Estado desconocido."),
    };
}
