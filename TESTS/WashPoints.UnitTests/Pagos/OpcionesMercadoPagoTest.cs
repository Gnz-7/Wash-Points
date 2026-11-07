using WashPoints.Infrastructure.MercadoPago;

namespace WashPoints.UnitTests.Pagos;

public sealed class OpcionesMercadoPagoTest
{
    [Fact]
    public void SinClavesDevuelveLasTresObligatorias()
    {
        var opciones = new OpcionesMercadoPago();

        var faltantes = opciones.ClavesFaltantes();

        Assert.Equal(
            ["Pagos:AccessToken", "Pagos:WebhookSecret", "Pagos:UrlNotificacion"],
            faltantes);
    }

    [Fact]
    public void ConLasTresClavesNoFaltaNada()
    {
        var opciones = new OpcionesMercadoPago
        {
            AccessToken = "TEST-123",
            WebhookSecret = "secreto",
            UrlNotificacion = "https://washpoints.app/api/webhooks/mercadopago",
        };

        Assert.Empty(opciones.ClavesFaltantes());
    }

    [Fact]
    public void LasClavesEnBlancoCuentanComoFaltantes()
    {
        var opciones = new OpcionesMercadoPago
        {
            AccessToken = "   ",
            WebhookSecret = "secreto",
            UrlNotificacion = "",
        };

        Assert.Equal(
            ["Pagos:AccessToken", "Pagos:UrlNotificacion"],
            opciones.ClavesFaltantes());
    }

    [Fact]
    public void UrlRetornoTieneUnValorPorDefecto()
    {
        Assert.Equal("https://washpoints.app/pago", new OpcionesMercadoPago().UrlRetorno);
    }
}
