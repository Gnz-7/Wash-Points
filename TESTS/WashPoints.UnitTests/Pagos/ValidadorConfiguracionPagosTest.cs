using WashPoints.Infrastructure.MercadoPago;

namespace WashPoints.UnitTests.Pagos;

public sealed class ValidadorConfiguracionPagosTest
{
    [Fact]
    public void EnProduccionSinClavesNoPermiteArrancar()
    {
        var opciones = new OpcionesMercadoPago();

        var error = Assert.Throws<InvalidOperationException>(
            () => ValidadorConfiguracionPagos.Validar(opciones, esProduccion: true));

        Assert.Contains("Pagos:AccessToken", error.Message);
        Assert.Contains("Pagos:WebhookSecret", error.Message);
        Assert.Contains("Pagos:UrlNotificacion", error.Message);
        Assert.DoesNotContain("TEST-", error.Message);
    }

    [Fact]
    public void EnProduccionConClavesArranca()
    {
        var opciones = new OpcionesMercadoPago
        {
            AccessToken = "TEST-123",
            WebhookSecret = "secreto",
            UrlNotificacion = "https://washpoints.app/api/webhooks/mercadopago",
        };

        ValidadorConfiguracionPagos.Validar(opciones, esProduccion: true);
    }

    [Fact]
    public void EnDesarrolloPermiteArrancarSinClaves()
    {
        ValidadorConfiguracionPagos.Validar(new OpcionesMercadoPago(), esProduccion: false);
    }
}
