using WashPoints.Infrastructure.MercadoPago;
using Xunit;

namespace WashPoints.UnitTests.MercadoPago;

/// <summary>
/// T-217: el <c>state</c> del OAuth de Mercado Pago es anti-CSRF (D-20.3).
///
/// Firma HMAC-SHA256 sobre <c>lavaderoId|nonce|expira</c> con clave derivada de
/// <c>MercadoPago:ClaveTokens</c>; el callback valida firma y expiración
/// (10 minutos). Un <c>state</c> inválido o vencido no toca el lavadero.
/// </summary>
public sealed class FirmadorEstadoOauthTest
{
    private const string ClaveTokens = "clave-de-prueba-32-bytes-en-base64===";

    private static FirmadorEstadoOauth CrearFirmador() =>
        new(ClaveTokens, TimeProvider.System);

    [Fact]
    public void UnEstadoFirmadoSeValidaYDevuelveElLavadero()
    {
        var firmador = CrearFirmador();
        var lavaderoId = Guid.NewGuid();

        var (estado, expira) = firmador.Firmar(lavaderoId);

        Assert.True(expira > DateTimeOffset.UtcNow);
        Assert.Equal(lavaderoId, firmador.Validar(estado));
    }

    [Fact]
    public void UnPayloadAlteradoNoSeValida()
    {
        var firmador = CrearFirmador();
        var (estado, _) = firmador.Firmar(Guid.NewGuid());
        var partes = estado.Split('.');
        // La misma firma con un payload inventado no debe validar.
        var alterado = $"{partes[0][0..^4]}AAAA.{partes[1]}";

        Assert.Null(firmador.Validar(alterado));
    }

    [Fact]
    public void UnEstadoConFormatoBasuraNoSeValida()
    {
        Assert.Null(CrearFirmador().Validar("no-es-un-estado"));
    }

    [Fact]
    public void UnEstadoVencidoNoSeValida()
    {
        var relojAtrasado = new RelojCorridoOauth(TimeSpan.FromMinutes(-11));
        var firmadorViejo = new FirmadorEstadoOauth(ClaveTokens, relojAtrasado);
        var estado = firmadorViejo.Firmar(Guid.NewGuid()).Estado;

        Assert.Null(CrearFirmador().Validar(estado));
    }

    [Fact]
    public void UnaFirmaConOtraClaveNoSeValida()
    {
        var estado = new FirmadorEstadoOauth(
            "otra-clave-de-prueba-32-bytes-en-base64", TimeProvider.System)
            .Firmar(Guid.NewGuid()).Estado;

        Assert.Null(CrearFirmador().Validar(estado));
    }

    private sealed class RelojCorridoOauth : TimeProvider
    {
        private readonly TimeSpan _desplazamiento;

        public RelojCorridoOauth(TimeSpan desplazamiento) => _desplazamiento = desplazamiento;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _desplazamiento;
    }
}
