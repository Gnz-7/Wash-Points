using WashPoints.Domain.Reembolsos;
using Xunit;

namespace WashPoints.UnitTests.Reembolsos;

/// <summary>
/// La entidad <see cref="Reembolso"/> es el registro contable de lo que se le
/// debe devolver al cliente (SPECS/slice-14 D-14.7). No habla con la pasarela:
/// sólo guarda qué se decidió, por qué y cuánto, para que el slice de pagos
/// pueda procesarlo después.
/// </summary>
public sealed class ReembolsoTest
{
    private static readonly DateTime Ahora = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TurnoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void RegistrarGuardaElTurnoElMontoYElMotivo()
    {
        var reembolso = Reembolso.Registrar(
            TurnoId,
            EstadoReembolso.Pendiente,
            MotivoReembolso.ClienteEnPlazo,
            1500m,
            Ahora);

        Assert.Equal(TurnoId, reembolso.TurnoId);
        Assert.Equal(EstadoReembolso.Pendiente, reembolso.Estado);
        Assert.Equal(MotivoReembolso.ClienteEnPlazo, reembolso.Motivo);
        Assert.Equal(1500m, reembolso.Monto);
        Assert.Equal(Ahora, reembolso.CreadoEn);
    }

    [Fact]
    public void UnReembolsoNaceSinProcesar()
    {
        var reembolso = Reembolso.Registrar(
            TurnoId,
            EstadoReembolso.Pendiente,
            MotivoReembolso.ClienteEnPlazo,
            1500m,
            Ahora);

        Assert.Null(reembolso.ProcesadoEn);
    }

    [Fact]
    public void UnReembolsoQueNoAplicaGuardaMontoCero()
    {
        var reembolso = Reembolso.Registrar(
            TurnoId,
            EstadoReembolso.NoAplica,
            MotivoReembolso.SinPago,
            0m,
            Ahora);

        Assert.Equal(EstadoReembolso.NoAplica, reembolso.Estado);
        Assert.Equal(MotivoReembolso.SinPago, reembolso.Motivo);
        Assert.Equal(0m, reembolso.Monto);
    }

    [Fact]
    public void UnMontoNegativoEsInvalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Reembolso.Registrar(
            TurnoId,
            EstadoReembolso.Pendiente,
            MotivoReembolso.ClienteEnPlazo,
            -1m,
            Ahora));
    }

    [Fact]
    public void UnReembolsoSinTurnoEsInvalido()
    {
        Assert.Throws<ArgumentException>(() => Reembolso.Registrar(
            Guid.Empty,
            EstadoReembolso.NoAplica,
            MotivoReembolso.SinPago,
            0m,
            Ahora));
    }
}
