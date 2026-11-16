using WashPoints.Domain.Catalogo;
using Xunit;

namespace WashPoints.UnitTests.Catalogo;

/// <summary>
/// T-216 (SPECS/slice-20, D-20.7/D-20.8): suscripción mensual mock y
/// vinculación de Mercado Pago sobre el dominio <c>Lavadero</c>.
///
/// CA-S20.8: el alta deja la suscripción pendiente (activa=false, vence=null);
/// el cobro mensual mock la activa. CA-S20.7/S20.12 (base): la vinculación
/// marca el estado y persiste los tokens cifrados; re-vincular reemplaza y
/// desvincular limpia.
/// </summary>
public sealed class LavaderoSuscripcionVinculacionTest
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ElAltaDejaLaSuscripcionPendiente()
    {
        var lavadero = Lavadero.Crear("Un Lavadero");

        Assert.False(lavadero.SuscripcionActiva);
        Assert.Null(lavadero.SuscripcionVenceEl);
    }

    [Fact]
    public void ActivarSuscripcion_FijaActivoYVencimiento()
    {
        var lavadero = Lavadero.Crear("Un Lavadero");
        var vence = Ahora.AddDays(30);

        lavadero.ActivarSuscripcion(vence);

        Assert.True(lavadero.SuscripcionActiva);
        Assert.Equal(vence, lavadero.SuscripcionVenceEl);
    }

    [Fact]
    public void ElLavaderoNaceDesvinculadoDeMercadoPago()
    {
        var lavadero = Lavadero.Crear("Un Lavadero");

        Assert.False(lavadero.MpVinculado);
        Assert.Null(lavadero.MpAccessToken);
        Assert.Null(lavadero.MpRefreshToken);
        Assert.Null(lavadero.MpUserId);
        Assert.Null(lavadero.MpVinculadoEl);
    }

    [Fact]
    public void VincularMercadoPago_MarcaVinculadoYPersisteLosDatos()
    {
        var lavadero = Lavadero.Crear("Un Lavadero");

        lavadero.VincularMercadoPago("token-cifrado", "refresh-cifrado", "mp-user-1", Ahora);

        Assert.True(lavadero.MpVinculado);
        Assert.Equal("token-cifrado", lavadero.MpAccessToken);
        Assert.Equal("refresh-cifrado", lavadero.MpRefreshToken);
        Assert.Equal("mp-user-1", lavadero.MpUserId);
        Assert.Equal(Ahora, lavadero.MpVinculadoEl);
    }

    [Fact]
    public void ReVincular_ReemplazaLosTokensAnteriores()
    {
        var lavadero = Lavadero.Crear("Un Lavadero");
        lavadero.VincularMercadoPago("token-viejo", "refresh-viejo", "user-viejo", Ahora);

        lavadero.VincularMercadoPago("token-nuevo", "refresh-nuevo", "user-nuevo", Ahora.AddDays(1));

        Assert.True(lavadero.MpVinculado);
        Assert.Equal("token-nuevo", lavadero.MpAccessToken);
        Assert.Equal("refresh-nuevo", lavadero.MpRefreshToken);
        Assert.Equal("user-nuevo", lavadero.MpUserId);
        Assert.Equal(Ahora.AddDays(1), lavadero.MpVinculadoEl);
    }

    [Fact]
    public void Desvincular_DejaFalseYLimpiaLosTokens()
    {
        var lavadero = Lavadero.Crear("Un Lavadero");
        lavadero.VincularMercadoPago("token", "refresh", "user", Ahora);

        lavadero.DesvincularMercadoPago("refresh fallido");

        Assert.False(lavadero.MpVinculado);
        Assert.Null(lavadero.MpAccessToken);
        Assert.Null(lavadero.MpRefreshToken);
        Assert.Null(lavadero.MpVinculadoEl);
    }
}
