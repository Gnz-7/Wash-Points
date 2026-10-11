using WashPoints.Domain.Puertos;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.UnitTests.Fakes;

/// <summary>
/// El doble simulado de la pasarela es el guardián de RB-1 en los tests: si el
/// fake devolviera <c>Aprobado</c> sin libreto, cualquier test podría confirmar
/// un turno sin pago y la suite mentiría.
///
/// Fuente: SPECS/slice-01-reserva-sena-mp.md T-12 (verificación RB-1).
/// </summary>
public sealed class PasarelaPagosFakeSimuladaTest
{
    private static PreferenciaPago Preferencia() => new(
        TurnoId: Guid.NewGuid(),
        Monto: 3000m,
        Descripcion: "Seña Wash Points",
        ReferenciaExterna: Guid.NewGuid().ToString());

    [Fact]
    public async Task SinLibretoVerificarDevuelvePendiente()
    {
        var pasarela = new PasarelaPagosFakeSimulada();

        var resultado = await pasarela.VerificarPagoAsync("pago-desconocido", CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Pendiente, resultado.Estado);
    }

    [Fact]
    public async Task LibretoDevuelveLoScripteado()
    {
        var pasarela = new PasarelaPagosFakeSimulada();
        var turnoId = Guid.NewGuid();
        pasarela.ScriptearVerificacion("pago-1", ResultadoVerificacionPago.Aprobado(turnoId, "pago-1"));

        var resultado = await pasarela.VerificarPagoAsync("pago-1", CancellationToken.None);

        Assert.Equal(EstadoVerificacionPago.Aprobado, resultado.Estado);
        Assert.Equal(turnoId, resultado.TurnoId);
    }

    [Fact]
    public async Task EnModoCaidaCrearPreferenciaFalla()
    {
        var pasarela = new PasarelaPagosFakeSimulada { EnModoCaida = true };

        await Assert.ThrowsAsync<PasarelaNoDisponibleException>(
            () => pasarela.CrearPreferenciaAsync(Preferencia(), CancellationToken.None));
    }

    [Fact]
    public async Task EnModoCaidaVerificarFalla()
    {
        var pasarela = new PasarelaPagosFakeSimulada { EnModoCaida = true };

        await Assert.ThrowsAsync<PasarelaNoDisponibleException>(
            () => pasarela.VerificarPagoAsync("pago-1", CancellationToken.None));
    }

    [Fact]
    public async Task CrearPreferenciaDevuelveUnIdNoNuloYRegistraLaSolicitud()
    {
        var pasarela = new PasarelaPagosFakeSimulada();

        var id = await pasarela.CrearPreferenciaAsync(Preferencia(), CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Single(pasarela.PreferenciasCreadas);
    }
}
