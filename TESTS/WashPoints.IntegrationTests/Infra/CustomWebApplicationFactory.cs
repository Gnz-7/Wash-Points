using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using WashPoints.Domain.Seguridad;
using WashPoints.Infrastructure.Seguridad;

namespace WashPoints.IntegrationTests.Infra;

/// <summary>
/// Fábrica de hosts de los tests de integración (slice-05, D-05.6).
///
/// Corre el pipeline REAL de autenticación: no hay bypass anónimo. El
/// <c>HttpClient</c> que devuelve <see cref="CreateClient"/> inyecta por defecto
/// un <c>Authorization: Bearer &lt;token válido&gt;</c> —un administrador del
/// lavadero que aparece en la ruta, o un cliente cuando la ruta no tiene
/// lavadero— de modo que la suite existente sigue ejercitando el pipeline
/// autenticado sin cambiar sus llamadas. Los helpers permiten pedir tokens
/// alternativos (cliente, sin token, firma inválida, expirado) para los casos
/// 401/403, y el lavadero de la ruta lo decide el propio request.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>Clave de firma de los tokens de prueba (no es un secreto real).</summary>
    public const string ClaveJwt = "clave-de-prueba-suficientemente-larga-para-hs256";
    public const string Emisor = "washpoints-tests";
    public const string Audiencia = "washpoints-app";

    private readonly string _cadenaConexion;
    private readonly Action<IWebHostBuilder>? _configurar;
    private readonly EmisorTokenJwt _emisor;

    /// <param name="cadenaConexion">Base de datos del contenedor del test.</param>
    /// <param name="configurar">
    /// Ajustes extra del host (settings o servicios), equivalentes a un
    /// <c>WithWebHostBuilder</c>: remover workers, reemplazar la pasarela, etc.
    /// </param>
    public CustomWebApplicationFactory(string cadenaConexion, Action<IWebHostBuilder>? configurar = null)
    {
        _cadenaConexion = cadenaConexion;
        _configurar = configurar;
        _emisor = new EmisorTokenJwt(ClaveJwt, Emisor, Audiencia, 60, TimeProvider.System);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _cadenaConexion);
        builder.UseSetting("Jwt:ClaveSecreta", ClaveJwt);
        builder.UseSetting("Jwt:Emisor", Emisor);
        builder.UseSetting("Jwt:Audiencia", Audiencia);
        _configurar?.Invoke(builder);
    }

    /// <summary>Token de administrador del lavadero indicado.</summary>
    public string TokenAdministrador(Guid lavaderoId) =>
        _emisor.Emitir(Guid.NewGuid(), RolUsuario.Administrador, lavaderoId).Token;

    /// <summary>
    /// Token de cliente. Con <paramref name="clienteId"/> lleva además el claim
    /// de vínculo con el cliente de negocio (slice-05b, D-06.2).
    /// </summary>
    public string TokenCliente(Guid? clienteId = null)
    {
        var usuarioId = Guid.NewGuid();
        if (clienteId is null)
        {
            return _emisor.Emitir(usuarioId, RolUsuario.Cliente, null).Token;
        }

        // El emisor de producción agrega el claim en T-59; acá se firma a mano
        // para poder probar la regla de propiedad antes de implementarla.
        var claims = new[]
        {
            new Claim(ClaimsWashPoints.Sub, usuarioId.ToString()),
            new Claim(ClaimsWashPoints.Rol, ClaimsWashPoints.Cliente),
            new Claim(ClaimsWashPoints.ClienteId, clienteId.Value.ToString()),
        };
        var clave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ClaveJwt));
        var credenciales = new SigningCredentials(clave, SecurityAlgorithms.HmacSha256);
        var ahora = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Emisor,
            audience: Audiencia,
            claims: claims,
            notBefore: ahora,
            expires: ahora.AddMinutes(60),
            signingCredentials: credenciales);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Token bien formado pero firmado con otra clave: firma inválida.</summary>
    public string TokenFirmaInvalida() =>
        new EmisorTokenJwt(
                "otra-clave-de-prueba-suficientemente-larga-para-hs256",
                Emisor,
                Audiencia,
                60,
                TimeProvider.System)
            .Emitir(Guid.NewGuid(), RolUsuario.Administrador, Guid.NewGuid())
            .Token;

    /// <summary>Token vencido: emitido con el reloj dos horas atrás.</summary>
    public string TokenExpirado() =>
        new EmisorTokenJwt(ClaveJwt, Emisor, Audiencia, 60, new RelojCorrido(TimeSpan.FromHours(-2)))
            .Emitir(Guid.NewGuid(), RolUsuario.Administrador, Guid.NewGuid())
            .Token;

    /// <summary>Cliente sin la inyección automática de sesión (para 401).</summary>
    public HttpClient CrearClienteSinToken() => base.CreateDefaultClient();

    /// <summary>Cliente con un token explícito, sin inyección automática.</summary>
    public HttpClient CrearClienteConToken(string token)
    {
        var cliente = base.CreateDefaultClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    /// <summary>Cliente con sesión de cliente (cualquier rol).</summary>
    public HttpClient CrearClienteDeCliente(Guid? clienteId = null) =>
        CrearClienteConToken(TokenCliente(clienteId));

    /// <summary>Cliente con sesión de administrador del lavadero indicado.</summary>
    public HttpClient CrearClienteAdmin(Guid lavaderoId) => CrearClienteConToken(TokenAdministrador(lavaderoId));

    /// <summary>
    /// Cliente base con la sesión inyectada por defecto (D-05.6). Oculta a
    /// propósito el <c>CreateClient</c> heredado para que los tests existentes
    /// sigan usando el pipeline autenticado sin cambiar sus llamadas.
    /// </summary>
    public new HttpClient CreateClient() =>
        base.CreateDefaultClient(new ManejadorTokenPorDefecto(this));

    private string TokenParaRuta(Guid? lavaderoId) =>
        lavaderoId is Guid id ? TokenAdministrador(id) : TokenCliente();

    /// <summary>
    /// Inyecta la sesión por defecto cuando el request no trae una propia. El
    /// lavadero sale de la ruta <c>/api/lavaderos/{id}</c>; sin él, la sesión es
    /// de cliente (válida para los endpoints de cliente).
    /// </summary>
    private sealed class ManejadorTokenPorDefecto : DelegatingHandler
    {
        private readonly CustomWebApplicationFactory _fabrica;

        public ManejadorTokenPorDefecto(CustomWebApplicationFactory fabrica) => _fabrica = fabrica;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage solicitud,
            CancellationToken ct)
        {
            if (solicitud.Headers.Authorization is null)
            {
                solicitud.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    _fabrica.TokenParaRuta(LavaderoDeLaRuta(solicitud.RequestUri)));
            }

            return base.SendAsync(solicitud, ct);
        }

        private static Guid? LavaderoDeLaRuta(Uri? uri)
        {
            if (uri is null)
            {
                return null;
            }

            var segmentos = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segmentos.Length - 1; i++)
            {
                if (segmentos[i] == "lavaderos" && Guid.TryParse(segmentos[i + 1], out var id))
                {
                    return id;
                }
            }

            return null;
        }
    }

    private sealed class RelojCorrido : TimeProvider
    {
        private readonly TimeSpan _desplazamiento;

        public RelojCorrido(TimeSpan desplazamiento) => _desplazamiento = desplazamiento;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _desplazamiento;
    }
}
