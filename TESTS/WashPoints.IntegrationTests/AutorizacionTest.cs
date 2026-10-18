using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-54: autorización server-side (CU-10, US-A09). No hay bypass anónimo: la
/// suite corre el pipeline real de autenticación (D-05.6).
///
/// CA-S5.3 — sin token, token mal firmado o expirado ⇒ 401.
/// CA-S5.4 — un cliente sobre un endpoint de administración ⇒ 403 rol_insuficiente.
/// CA-S5.7 — la decisión es del servidor: un cliente sí accede a los endpoints
///           de cliente (disponibilidad, reserva).
///
/// Ver SPECS/slice-05-auth-y-permisos.md T-54 y §5.2.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class AutorizacionTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public AutorizacionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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
        new(_baseDeDatos.CadenaConexion);

    private async Task<Guid> SembrarLavaderoAsync(string nombre = "Lavadero Central")
    {
        await using var contexto = CrearContexto();
        var lavadero = Lavadero.Crear(nombre);
        contexto.Lavaderos.Add(lavadero);
        await contexto.SaveChangesAsync();
        return lavadero.Id;
    }

    private static object CuerpoPuesto(string nombre) => new { nombre };

    [Fact]
    public async Task SinTokenSobreEndpointDeAdministracionDevuelve401()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteSinToken();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task SinTokenSobreEndpointDeClienteDevuelve401()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteSinToken();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T10:00:00&hasta=2026-10-10T12:00:00");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task TokenConFirmaInvalidaDevuelve401()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteConToken(api.TokenFirmaInvalida());

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task TokenExpiradoDevuelve401()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteConToken(api.TokenExpirado());

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task TokenDeClienteSobreEndpointDeAdministracionDevuelve403RolInsuficiente()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("rol_insuficiente", error!.error);
    }

    [Fact]
    public async Task TokenDeClienteSobreEndpointDeClienteEsAceptado()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T10:00:00&hasta=2026-10-10T12:00:00");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task TokenVigenteDeAdministracionEsAceptado()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(lavaderoId);

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    private sealed record RespuestaError(string error);
}
