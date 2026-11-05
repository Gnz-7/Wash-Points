using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-132: catálogo de tipos de lavado (CU-05) end-to-end sobre HTTP.
///
/// CA-13.1 — crear un tipo lo deja activo y aparece en el listado.
/// CA-13.2 — una seña en cero se rechaza con 400 y no persiste.
/// CA-13.3 — nombre duplicado en el lavadero da 409 `tipo_ya_existe`.
/// CA-13.4 — desactivar con turnos vigentes da 409 `tipo_con_turnos_activos`.
/// CA-13.5 — un turno terminal no bloquea la desactivación, aunque sea futuro.
/// CA-13.6 — editar cambia nombre, duración y precios.
/// CA-13.7 — un tipo de otro lavadero es indistinguible de inexistente (404).
/// CA-13.8 — activar reactiva el tipo.
///
/// Ver SPECS/slice-13-catalogo-onboarding.md T-132 y §5.3.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class GestionTiposLavadoTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public GestionTiposLavadoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion));

    private async Task<Guid> SembrarLavaderoAsync(string nombre = "Lavadero Central")
    {
        await using var contexto = CrearContexto();
        var lavadero = Lavadero.Crear(nombre);
        contexto.Lavaderos.Add(lavadero);
        await contexto.SaveChangesAsync();
        return lavadero.Id;
    }

    private async Task<Guid> SembrarTipoAsync(
        Guid lavaderoId,
        string nombre = "Lavado básico")
    {
        await using var contexto = CrearContexto();
        var tipo = TipoLavado.Crear(lavaderoId, nombre, 30, 8000m, 1500m);
        contexto.TiposLavado.Add(tipo);
        await contexto.SaveChangesAsync();
        return tipo.Id;
    }

    /// <summary>Siembra un turno del tipo con el estado indicado y rango futuro.</summary>
    private async Task SembrarTurnoAsync(
        Guid lavaderoId,
        Guid tipoId,
        EstadoTurno estado,
        int horaInicio = 10)
    {
        await using var contexto = CrearContexto();

        var puesto = Puesto.Crear(lavaderoId, "Puesto 1");
        contexto.Puestos.Add(puesto);

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse($"2027-06-15T{horaInicio:00}:00:00"));
        var turno = Turno.Crear(
            lavaderoId, puesto.Id, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 1500m, precioTotal: 15000m);
        turno.TransicionarA(estado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
    }

    private async Task<bool> TipoActivoAsync(Guid tipoId) =>
        await _baseDeDatos.ConsultarEscalarAsync<bool>(
            $"SELECT activo FROM tipos_lavado WHERE id = '{tipoId}'");

    private static object CuerpoTipo(
        string nombre = "Lavado básico",
        int duracionMinutos = 30,
        decimal precioTotal = 8000m,
        decimal precioSena = 1500m) =>
        new { nombre, duracionMinutos, precioTotal, precioSena };

    [Fact]
    public async Task CrearTipoQuedaActivoYApareceEnElListado()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado", CuerpoTipo());

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<RespuestaTipo>();
        Assert.NotNull(creado);
        Assert.True(creado!.activo);
        Assert.Equal(30, creado.duracionMinutos);
        Assert.Equal(8000m, creado.precioTotal);
        Assert.Equal(1500m, creado.precioSena);

        var listado = await cliente.GetFromJsonAsync<List<RespuestaTipo>>(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado");
        Assert.NotNull(listado);
        Assert.Contains(listado!, t => t.id == creado.id);
    }

    [Fact]
    public async Task CrearTipoConSenaEnCeroDevuelve400()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado",
            CuerpoTipo(precioSena: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task NombreDuplicadoDevuelve409()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        await SembrarTipoAsync(lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado", CuerpoTipo());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("tipo_ya_existe", error!.error);
    }

    [Fact]
    public async Task DesactivarTipoSinTurnosLoInactiva()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var tipoId = await SembrarTipoAsync(lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado/{tipoId}/desactivar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(await TipoActivoAsync(tipoId));
    }

    [Fact]
    public async Task DesactivarTipoConTurnoVigenteDevuelve409()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var tipoId = await SembrarTipoAsync(lavaderoId);
        await SembrarTurnoAsync(lavaderoId, tipoId, EstadoTurno.Confirmado);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado/{tipoId}/desactivar", null);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("tipo_con_turnos_activos", error!.error);
        Assert.True(await TipoActivoAsync(tipoId));
    }

    [Fact]
    public async Task UnTurnoTerminalNoBloqueaLaDesactivacion()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var tipoId = await SembrarTipoAsync(lavaderoId);
        await SembrarTurnoAsync(lavaderoId, tipoId, EstadoTurno.Cancelado);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado/{tipoId}/desactivar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(await TipoActivoAsync(tipoId));
    }

    [Fact]
    public async Task EditarTipoCambiaNombreDuracionYPrecios()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var tipoId = await SembrarTipoAsync(lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado/{tipoId}",
            CuerpoTipo("Lavado completo", 60, 15000m, 2500m));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var editado = await respuesta.Content.ReadFromJsonAsync<RespuestaTipo>();
        Assert.Equal("Lavado completo", editado!.nombre);
        Assert.Equal(60, editado.duracionMinutos);
        Assert.Equal(15000m, editado.precioTotal);
        Assert.Equal(2500m, editado.precioSena);
    }

    [Fact]
    public async Task EditarUnTipoDeOtroLavaderoDevuelve404()
    {
        var lavaderoA = await SembrarLavaderoAsync("Lavadero A");
        var lavaderoB = await SembrarLavaderoAsync("Lavadero B");
        var tipoDeB = await SembrarTipoAsync(lavaderoB);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoA}/tipos-lavado/{tipoDeB}",
            CuerpoTipo("Otro nombre", 60, 15000m, 2500m));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("tipo_no_encontrado", error!.error);
    }

    [Fact]
    public async Task ActivarReactivaElTipo()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var tipoId = await SembrarTipoAsync(lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        await cliente.PostAsync($"/api/lavaderos/{lavaderoId}/tipos-lavado/{tipoId}/desactivar", null);
        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/tipos-lavado/{tipoId}/activar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(await TipoActivoAsync(tipoId));
    }

    private sealed record RespuestaTipo(
        Guid id,
        string nombre,
        int duracionMinutos,
        decimal precioTotal,
        decimal precioSena,
        bool activo);

    private sealed record RespuestaError(string error);
}
