using WashPoints.Domain.Turnos;
using Xunit;

namespace WashPoints.UnitTests.Espontanea;

/// <summary>
/// Fábrica de demanda espontánea (SPECS/slice-03 T-35, CU-07 CA-1.1/CA-1.5).
///
/// La espontánea es la única vía que nace fuera de `pendiente_pago`: ocupa el
/// puesto de inmediato y **no** exige seña ni comprobante. Es el test de
/// regresión de RB-2 en el dominio: si alguien hiciera nacer la espontánea en
/// `pendiente_pago` (como decía ARCHITECTURE §5 antes del slice-03), falla acá.
/// </summary>
public sealed class TurnoEspontaneoTest
{
    [Fact]
    public void CrearEspontaneo_NaceEnCurso()
    {
        var turno = CrearEspontaneo();

        Assert.Equal(EstadoTurno.EnCurso, turno.Estado);
    }

    [Fact]
    public void CrearEspontaneo_EsOrigenEspontaneaYSinSena()
    {
        var turno = CrearEspontaneo();

        Assert.Equal(OrigenTurno.Espontanea, turno.Origen);
        Assert.Equal(0m, turno.PrecioSena);
    }

    [Fact]
    public void CrearEspontaneo_NoTieneIdempotenciaNiVencimiento()
    {
        var turno = CrearEspontaneo();

        // La espontánea no proviene de un intento de reserva: no lleva clave de
        // idempotencia (índice parcial `idempotency_key IS NOT NULL`), ni TTL de
        // seña, ni marca de confirmación (nunca pasó por pago).
        Assert.Null(turno.IdempotencyKey);
        Assert.Null(turno.ExpiraEn);
        Assert.Null(turno.ConfirmadoEn);
    }

    [Fact]
    public void CrearEspontaneo_OcupaPuesto()
    {
        var turno = CrearEspontaneo();

        Assert.True(turno.OcupaPuesto);
    }

    private static Turno CrearEspontaneo()
    {
        var inicioUtc = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);

        return Turno.CrearEspontaneo(
            lavaderoId: Guid.NewGuid(),
            puestoId: Guid.NewGuid(),
            clienteId: Guid.NewGuid(),
            tipoLavadoId: Guid.NewGuid(),
            inicioUtc: inicioUtc,
            finUtc: inicioUtc.AddMinutes(30));
    }
}
