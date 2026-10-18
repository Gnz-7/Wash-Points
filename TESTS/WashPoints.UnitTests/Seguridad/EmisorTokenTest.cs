using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WashPoints.Domain.Seguridad;
using WashPoints.Infrastructure.Seguridad;
using Xunit;

namespace WashPoints.UnitTests.Seguridad;

/// <summary>
/// Emisión de sesiones JWT (SPECS/slice-05 T-52, D-05.2): firma HS256, claims
/// `sub`/`rol`/`lavaderoId` y vigencia configurable.
/// </summary>
public sealed class EmisorTokenTest
{
    private const string Clave = "clave-de-prueba-suficientemente-larga-para-hs256";
    private const string Emisor = "washpoints-tests";
    private const string Audiencia = "washpoints-app";
    private const int MinutosVida = 30;

    private static EmisorTokenJwt EmisorDe(DateTimeOffset ahora) =>
        new(Clave, Emisor, Audiencia, MinutosVida, new RelojFijo(ahora));

    [Fact]
    public void EmitirIncluyeElIdElRolYElLavadero()
    {
        var usuarioId = Guid.NewGuid();
        var lavaderoId = Guid.NewGuid();
        var emisor = EmisorDe(DateTimeOffset.UtcNow);

        var sesion = emisor.Emitir(usuarioId, RolUsuario.Administrador, lavaderoId);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(sesion.Token);

        Assert.Equal(usuarioId.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("administrador", jwt.Claims.Single(c => c.Type == "rol").Value);
        Assert.Equal(lavaderoId.ToString(), jwt.Claims.Single(c => c.Type == "lavaderoId").Value);
        Assert.Equal(Emisor, jwt.Issuer);
        Assert.Equal(new[] { Audiencia }, jwt.Audiences);
    }

    [Fact]
    public void UnClienteNoLlevaClaimDeLavadero()
    {
        var emisor = EmisorDe(DateTimeOffset.UtcNow);

        var sesion = emisor.Emitir(Guid.NewGuid(), RolUsuario.Cliente, null);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(sesion.Token);

        Assert.Equal("cliente", jwt.Claims.Single(c => c.Type == "rol").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "lavaderoId");
    }

    [Fact]
    public void LaVigenciaRespetaLosMinutosConfigurados()
    {
        var ahora = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        var emisor = EmisorDe(ahora);

        var sesion = emisor.Emitir(Guid.NewGuid(), RolUsuario.Cliente, null);

        var esperado = ahora.UtcDateTime.AddMinutes(MinutosVida);
        Assert.Equal(esperado, sesion.ExpiraEnUtc);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(sesion.Token);
        Assert.Equal(esperado, jwt.ValidTo);
    }

    [Fact]
    public void ElTokenSeValidaConLaClaveYLosParametrosEsperados()
    {
        var usuarioId = Guid.NewGuid();
        var emisor = EmisorDe(DateTimeOffset.UtcNow);
        var sesion = emisor.Emitir(usuarioId, RolUsuario.Administrador, Guid.NewGuid());

        var manejador = new JwtSecurityTokenHandler();
        manejador.InboundClaimTypeMap.Clear();
        var parametros = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Emisor,
            ValidateAudience = true,
            ValidAudience = Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Clave)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        var principal = manejador.ValidateToken(sesion.Token, parametros, out _);

        Assert.Equal(usuarioId.ToString(), principal.FindFirst("sub")!.Value);
    }

    [Fact]
    public void UnTokenFirmadoConOtraClaveNoSeValida()
    {
        var sesion = EmisorDe(DateTimeOffset.UtcNow)
            .Emitir(Guid.NewGuid(), RolUsuario.Cliente, null);

        var manejador = new JwtSecurityTokenHandler();
        var parametros = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Emisor,
            ValidateAudience = true,
            ValidAudience = Audiencia,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("otra-clave-distinta-pero-larga-igual-1234")),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        Assert.ThrowsAny<SecurityTokenException>(
            () => manejador.ValidateToken(sesion.Token, parametros, out _));
    }

    [Fact]
    public void UnaClaveDemasiadoCortaSeRechaza()
    {
        Assert.Throws<ArgumentException>(
            () => new EmisorTokenJwt("corta", Emisor, Audiencia, MinutosVida, new RelojFijo(DateTimeOffset.UtcNow)));
    }

    private sealed class RelojFijo : TimeProvider
    {
        private readonly DateTimeOffset _ahora;

        public RelojFijo(DateTimeOffset ahora) => _ahora = ahora;

        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
