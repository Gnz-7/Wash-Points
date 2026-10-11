using WashPoints.Domain.Comprobantes;
using Xunit;

namespace WashPoints.UnitTests.Comprobantes;

/// <summary>
/// Matriz de canje del comprobante: válido, ya usado y vencido (CA-S2.5,
/// CA-S2.7; SPECS/slice-02 §3, T-25). Es el test de regresión de RB-3 a nivel
/// de dominio: un comprobante no se canjea dos veces ni estando vencido.
/// </summary>
public sealed class MaquinaEstadosComprobanteTest
{
    private static readonly DateTime Ahora = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

    private static Comprobante Emitido(DateTime? expiraEn = null) =>
        Comprobante.Emitir(
            turnoId: Guid.NewGuid(),
            hash: new string('a', 64),
            tokenCifrado: "token-cifrado",
            expiraEnUtc: expiraEn ?? Ahora.AddHours(24),
            creadoEnUtc: Ahora);

    [Fact]
    public void UnComprobanteRecienEmitidoEsCanjeable()
    {
        var comprobante = Emitido();

        comprobante.AsegurarCanjeable(Ahora);

        Assert.False(comprobante.Usado);
    }

    [Fact]
    public void RegistrarCanjeMarcaUsadoYLaViaOffline()
    {
        var comprobante = Emitido();

        comprobante.RegistrarCanje(Ahora, offline: true);

        Assert.True(comprobante.Usado);
        Assert.Equal(Ahora, comprobante.UsadoAt);
        Assert.True(comprobante.ValidadoOffline);
    }

    [Fact]
    public void UnComprobanteYaUsadoNoSeVuelveACanjear()
    {
        var comprobante = Emitido();
        comprobante.RegistrarCanje(Ahora);

        Assert.Throws<ComprobanteYaUtilizadoException>(() => comprobante.AsegurarCanjeable(Ahora));
    }

    [Fact]
    public void UnComprobanteVencidoNoSeCanjea()
    {
        var comprobante = Emitido(expiraEn: Ahora.AddMinutes(-1));

        Assert.Throws<ComprobanteVencidoException>(() => comprobante.AsegurarCanjeable(Ahora));
    }

    [Fact]
    public void ElVencimientoEsInclusivo()
    {
        // A la hora exacta de vencimiento el comprobante ya no es canjeable.
        var comprobante = Emitido(expiraEn: Ahora);

        Assert.Throws<ComprobanteVencidoException>(() => comprobante.AsegurarCanjeable(Ahora));
    }

    [Fact]
    public void YaUsadoTienePrioridadSobreVencido()
    {
        // US-A04 CA-4.2: si ya se canjeó, el motivo es "ya utilizado" aunque
        // además haya expirado. Define el código de error del canje concurrente.
        var comprobante = Emitido(expiraEn: Ahora.AddMinutes(-1));
        comprobante.RegistrarCanje(Ahora.AddMinutes(-5));

        Assert.Throws<ComprobanteYaUtilizadoException>(() => comprobante.AsegurarCanjeable(Ahora));
    }

    [Fact]
    public void RegistrarCanjeExigeInstanteUtc()
    {
        var comprobante = Emitido();

        Assert.Throws<ArgumentException>(() =>
            comprobante.RegistrarCanje(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Local)));
    }
}
