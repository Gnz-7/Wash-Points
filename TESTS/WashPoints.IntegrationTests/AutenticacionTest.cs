using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WashPoints.Domain.Seguridad;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Seguridad;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-53: login (CU-10) end-to-end sobre HTTP.
///
/// CA-S5.1 — un login válido emite una sesión con rol y, si es administrador, el
///           lavadero al que pertenece (un cliente viaja sin lavadero).
/// CA-S5.2 / CA-9.4 — usuario inexistente y contraseña errónea devuelven la misma
///           respuesta genérica 401 credenciales_invalidas; un usuario inactivo
///           responde igual.
///
/// El usuario se siembra por SQL con el hash PBKDF2 real (D-05.9): nunca se
/// persiste el claro. Ver SPECS/slice-05-auth-y-permisos.md T-53 y §5.1.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class AutenticacionTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string ClaveJwt = "clave-de-prueba-suficientemente-larga-para-hs256";
    private const string Emisor = "washpoints-tests";
    private const string Audiencia = "washpoints-app";

    private readonly ContenedorPostgres _baseDeDatos;

    public AutenticacionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos, usuarios CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.UseSetting("Jwt:ClaveSecreta", ClaveJwt);
            builder.UseSetting("Jwt:Emisor", Emisor);
            builder.UseSetting("Jwt:Audiencia", Audiencia);
        });

    /// <summary>Siembra una credencial con hash PBKDF2 real (nunca el claro).</summary>
    private async Task<Guid> SembrarUsuarioAsync(
        string nombreUsuario,
        string contrasena,
        RolUsuario rol,
        Guid? lavaderoId,
        bool activo = true)
    {
        var hash = new HasheadorContrasenaPbkdf2().Hashear(contrasena);
        var id = Guid.NewGuid();
        var rolTexto = rol == RolUsuario.Administrador ? "administrador" : "cliente";
        var lavaderoSql = lavaderoId is { } lavadero ? $"'{lavadero}'" : "NULL";

        await _baseDeDatos.EjecutarSqlAsync($"""
            INSERT INTO usuarios (id, nombre_usuario, hash_contrasena, rol, lavadero_id, activo, creado_en)
            VALUES ('{id}', '{nombreUsuario}', '{hash}', '{rolTexto}', {lavaderoSql},
                    {(activo ? "true" : "false")}, now());
            """);

        return id;
    }

    /// <summary>Valida la firma del token y devuelve sus claims sin mapear.</summary>
    private static ClaimsPrincipal ValidarToken(string token)
    {
        var manejador = new JwtSecurityTokenHandler();
        manejador.InboundClaimTypeMap.Clear();

        return manejador.ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Emisor,
                ValidateAudience = true,
                ValidAudience = Audiencia,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ClaveJwt)),
                ValidateLifetime = true,
            },
            out _);
    }

    [Fact]
    public async Task LoginValidoEmiteSesionConRolYLavadero()
    {
        var lavaderoId = Guid.NewGuid();
        var usuarioId = await SembrarUsuarioAsync(
            "admin", "secreta123", RolUsuario.Administrador, lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            "/api/auth/login", new { nombreUsuario = "admin", contrasena = "secreta123" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var sesion = await respuesta.Content.ReadFromJsonAsync<RespuestaLogin>();
        Assert.NotNull(sesion);
        Assert.Equal("administrador", sesion!.rol);
        Assert.Equal(lavaderoId, sesion.lavaderoId);
        Assert.True(sesion.expiraEn > DateTime.UtcNow);

        var identidad = ValidarToken(sesion.token);
        Assert.Equal("administrador", identidad.FindFirst("rol")!.Value);
        Assert.Equal(lavaderoId.ToString(), identidad.FindFirst("lavaderoId")!.Value);
        Assert.Equal(usuarioId.ToString(), identidad.FindFirst("sub")!.Value);
    }

    [Fact]
    public async Task UsuarioInexistenteYClaveErroneaRespondenIgual()
    {
        await SembrarUsuarioAsync("admin", "secreta123", RolUsuario.Administrador, Guid.NewGuid());
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var inexistente = await cliente.PostAsJsonAsync(
            "/api/auth/login", new { nombreUsuario = "no-existe", contrasena = "secreta123" });
        var claveErronea = await cliente.PostAsJsonAsync(
            "/api/auth/login", new { nombreUsuario = "admin", contrasena = "incorrecta" });

        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, claveErronea.StatusCode);

        var cuerpoInexistente = await inexistente.Content.ReadAsStringAsync();
        var cuerpoClaveErronea = await claveErronea.Content.ReadAsStringAsync();
        Assert.Equal(cuerpoInexistente, cuerpoClaveErronea);

        var error = JsonSerializer.Deserialize<RespuestaError>(cuerpoInexistente);
        Assert.Equal("credenciales_invalidas", error!.error);
    }

    [Fact]
    public async Task UsuarioInactivoDevuelveCredencialesInvalidas()
    {
        await SembrarUsuarioAsync(
            "admin", "secreta123", RolUsuario.Administrador, Guid.NewGuid(), activo: false);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            "/api/auth/login", new { nombreUsuario = "admin", contrasena = "secreta123" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("credenciales_invalidas", error!.error);
    }

    [Fact]
    public async Task LoginDeClienteNoTraeLavadero()
    {
        await SembrarUsuarioAsync("cliente", "secreta123", RolUsuario.Cliente, lavaderoId: null);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            "/api/auth/login", new { nombreUsuario = "cliente", contrasena = "secreta123" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var sesion = await respuesta.Content.ReadFromJsonAsync<RespuestaLogin>();
        Assert.NotNull(sesion);
        Assert.Equal("cliente", sesion!.rol);
        Assert.Null(sesion.lavaderoId);

        var identidad = ValidarToken(sesion.token);
        Assert.Equal("cliente", identidad.FindFirst("rol")!.Value);
        Assert.Null(identidad.FindFirst("lavaderoId"));
    }

    private sealed record RespuestaLogin(string token, DateTime expiraEn, string rol, Guid? lavaderoId);

    private sealed record RespuestaError(string error);
}
