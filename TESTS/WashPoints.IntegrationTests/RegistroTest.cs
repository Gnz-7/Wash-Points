using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Alta de cuentas (slice-08, CU-10 / US-A09): registro anónimo de cliente y
/// de administrador con token inmediato. Verifica CA-S8.1…CA-S8.7: claims
/// correctos en el token, lavadero visible en la búsqueda, unicidad del
/// nombre de usuario, errores granulares, contraseña nunca en la respuesta,
/// atomicidad del alta de administrador y ausencia de claves nulas.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class RegistroTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const string Nombre = "Ana Pérez";
    private const string Usuario = "ana.perez";
    private const string Contrasena = "unaClaveSegura1";
    private const double LatitudCentro = -34.6037;
    private const double LongitudCentro = -58.3816;

    private readonly ContenedorPostgres _baseDeDatos;

    public RegistroTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, usuarios, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                foreach (var tipo in new[]
                         {
                             typeof(WashPoints.Api.WorkerReservas),
                             typeof(WashPoints.Api.PublicadorOutboxWorker),
                             typeof(WashPoints.Api.WorkerAgenda),
                         })
                {
                    var descriptor = servicios.SingleOrDefault(d => d.ImplementationType == tipo);
                    if (descriptor is not null)
                    {
                        servicios.Remove(descriptor);
                    }
                }
            });
        });

    private static RegistroClienteCuerpo CuerpoCliente(
        string usuario = Usuario,
        string nombre = Nombre,
        string contrasena = Contrasena) =>
        new(nombre, usuario, contrasena);

    private static RegistroAdministradorCuerpo CuerpoAdmin(
        string usuario = Usuario,
        string nombreLavadero = "Lavadero Nuevo",
        double latitud = LatitudCentro,
        double longitud = LongitudCentro,
        string? direccion = "Av. Rivadavia 1234") =>
        new(Nombre, usuario, Contrasena, nombreLavadero, latitud, longitud, direccion);

    // CA-S8.1: el alta de cliente devuelve 201 con un token que ya sirve.
    [Fact]
    public async Task UnClienteSeRegistraYRecibeUnTokenUtilizable()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync("/api/registro/cliente", CuerpoCliente());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var registro = await respuesta.Content.ReadFromJsonAsync<RegistroClienteRespuesta>();
        Assert.NotNull(registro);
        Assert.NotEqual(Guid.Empty, registro!.UsuarioId);
        Assert.NotEqual(Guid.Empty, registro.ClienteId);
        Assert.Equal("cliente", registro.Rol);
        Assert.False(string.IsNullOrWhiteSpace(registro.Token));

        using var conToken = api.CrearClienteConToken(registro.Token);
        var cercanos = await conToken.GetAsync(
            $"/api/lavaderos/cercanos?lat={LatitudCentro.ToString(CultureInfo.InvariantCulture)}" +
            $"&lon={LongitudCentro.ToString(CultureInfo.InvariantCulture)}&radioMetros=1000");

        Assert.Equal(HttpStatusCode.OK, cercanos.StatusCode);
    }

    // CA-S8.2: el alta de administrador devuelve su lavadero y un token con
    // ownership sobre él.
    [Fact]
    public async Task UnAdministradorSeRegistraYRecibeUnTokenUtilizable()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync("/api/registro/administrador", CuerpoAdmin());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var registro = await respuesta.Content.ReadFromJsonAsync<RegistroAdministradorRespuesta>();
        Assert.NotNull(registro);
        Assert.NotEqual(Guid.Empty, registro!.LavaderoId);
        Assert.Equal("administrador", registro.Rol);
        Assert.False(string.IsNullOrWhiteSpace(registro.Token));

        using var conToken = api.CrearClienteConToken(registro.Token);
        var agenda = await conToken.GetAsync($"/api/lavaderos/{registro.LavaderoId}/agenda");

        Assert.Equal(HttpStatusCode.OK, agenda.StatusCode);
    }

    // CA-S8.2: el lavadero creado en el registro ya es descubrible por CU-01.
    [Fact]
    public async Task ElNuevoLavaderoApareceEnLaBusquedaPorProximidad()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var admin = await anonimo.PostAsJsonAsync("/api/registro/administrador", CuerpoAdmin());
        var lavadero = await admin.Content.ReadFromJsonAsync<RegistroAdministradorRespuesta>();
        Assert.NotNull(lavadero);

        var cliente = await anonimo.PostAsJsonAsync(
            "/api/registro/cliente", CuerpoCliente(usuario: "cliente.busqueda"));
        var sesion = await cliente.Content.ReadFromJsonAsync<RegistroClienteRespuesta>();
        Assert.NotNull(sesion);

        using var conToken = api.CrearClienteConToken(sesion!.Token);
        var cercanos = await conToken.GetAsync(
            $"/api/lavaderos/cercanos?lat={LatitudCentro.ToString(CultureInfo.InvariantCulture)}" +
            $"&lon={LongitudCentro.ToString(CultureInfo.InvariantCulture)}&radioMetros=5000");

        Assert.Equal(HttpStatusCode.OK, cercanos.StatusCode);
        var cuerpo = await cercanos.Content.ReadFromJsonAsync<RespuestaBusqueda>();
        Assert.NotNull(cuerpo);
        Assert.Contains(cuerpo!.Resultados, resultado => resultado.LavaderoId == lavadero!.LavaderoId);
    }

    // CA-S8.4: dos cuentas no pueden compartir nombre de usuario.
    [Fact]
    public async Task UnUsuarioDuplicadoDevuelve409()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var primero = await anonimo.PostAsJsonAsync("/api/registro/cliente", CuerpoCliente());
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await anonimo.PostAsJsonAsync("/api/registro/administrador", CuerpoAdmin());

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var error = await segundo.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("usuario_ya_existe", error!.Error);
    }

    // CA-S8.5: los errores identifican el campo inválido.
    [Fact]
    public async Task UnNombreVacioDevuelve400()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync(
            "/api/registro/cliente", CuerpoCliente(nombre: "   "));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("nombre_invalido", error!.Error);
    }

    // CA-S8.5: la contraseña exige un mínimo de 8 caracteres.
    [Fact]
    public async Task UnaContrasenaCortaDevuelve400()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync(
            "/api/registro/cliente", CuerpoCliente(contrasena: "corta"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("contrasena_invalida", error!.Error);
    }

    // CA-S8.3: la contraseña no viaja en la respuesta, ni en claro ni hasheada.
    [Fact]
    public async Task LaRespuestaNoExponeLaContrasena()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync("/api/registro/cliente", CuerpoCliente());
        var crudo = await respuesta.Content.ReadAsStringAsync();

        // El 404/400 no basta: la respuesta tiene que ser el alta exitosa, y aun
        // así no debe filtrar la contraseña ni su hash.
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.DoesNotContain(Contrasena, crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contrasena", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pbkdf2", crudo, StringComparison.OrdinalIgnoreCase);
    }

    // CA-S8.6: si el usuario no se puede crear, el lavadero tampoco queda.
    [Fact]
    public async Task ElAltaDeAdministradorEsAtomica()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var primero = await anonimo.PostAsJsonAsync("/api/registro/cliente", CuerpoCliente());
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await anonimo.PostAsJsonAsync(
            "/api/registro/administrador", CuerpoAdmin(nombreLavadero: "Lavadero Huérfano"));
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);

        await using var contexto = CrearContexto();
        Assert.Equal(0, await contexto.Lavaderos.CountAsync());
    }

    // CA-S8.7: la respuesta del cliente no incluye lavaderoId.
    [Fact]
    public async Task LaRespuestaDeClienteNoTraeLavaderoId()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync("/api/registro/cliente", CuerpoCliente());
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

        Assert.False(documento.RootElement.TryGetProperty("lavaderoId", out _));
    }

    // CA-S8.7: la respuesta del administrador no incluye clienteId.
    [Fact]
    public async Task LaRespuestaDeAdministradorNoTraeClienteId()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.PostAsJsonAsync(
            "/api/registro/administrador", CuerpoAdmin(usuario: "admin.sin.cliente"));
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

        Assert.False(documento.RootElement.TryGetProperty("clienteId", out _));
    }

    private sealed record RegistroClienteCuerpo(string Nombre, string Usuario, string Contrasena);

    private sealed record RegistroAdministradorCuerpo(
        string Nombre,
        string Usuario,
        string Contrasena,
        string NombreLavadero,
        double Latitud,
        double Longitud,
        string? Direccion = null);

    private sealed record RegistroClienteRespuesta(
        Guid UsuarioId,
        Guid ClienteId,
        string Rol,
        string Token,
        DateTime ExpiraEn);

    private sealed record RegistroAdministradorRespuesta(
        Guid UsuarioId,
        Guid LavaderoId,
        string Rol,
        string Token,
        DateTime ExpiraEn);

    private sealed record RespuestaError(string Error);

    private sealed record RespuestaBusqueda(List<LavaderoCercanoDto> Resultados);

    private sealed record LavaderoCercanoDto(Guid LavaderoId, string Nombre);
}
