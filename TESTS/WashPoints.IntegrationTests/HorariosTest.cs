using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-191: horarios de atención y servicios por puesto end-to-end
/// (SPECS/slice-17 §4.1 y §4.2, D-17.1, D-17.2, D-17.3 y D-17.4).
///
/// CA-17.3  — el panel lee y reemplaza la semana completa del lavadero.
/// CA-17.5  — una semana con franjas superpuestas se rechaza con 400
///            `franja_invalida`; un cuerpo sin `franjas` también.
/// CA-17.7  — un lavadero inexistente no se configura: 404.
/// CA-17.10 — el administrador asigna los servicios que atiende un puesto y el
///            catálogo los publica; sin filas el puesto atiende todos.
/// CA-17.11 — un tipo de lavado de otro lavadero se rechaza con 404 y un
///            administrador ajeno recibe 403.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class HorariosTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public HorariosTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, usuarios,
                           clientes, puestos, tipos_lavado, horarios_atencion,
                           puestos_tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion));

    /// <summary>
    /// Siembra un lavadero con un puesto y dos tipos de lavado, más las franjas
    /// indicadas. Devuelve los identificadores y un segundo lavadero ajeno.
    /// </summary>
    private async Task<(
        Guid LavaderoId,
        Guid PuestoId,
        Guid TipoExteriorId,
        Guid TipoInteriorId,
        Guid LavaderoAjenoId)> SembrarAsync(params (int Dia, string Apertura, string Cierre)[] franjas)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var ajeno = Lavadero.Crear("Lavadero Ajeno");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var exterior = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var interior = TipoLavado.Crear(lavadero.Id, "Interior", 60, 20000m, 4000m);

        contexto.AddRange(lavadero, ajeno, puesto, exterior, interior);

        foreach (var (dia, apertura, cierre) in franjas)
        {
            contexto.HorariosAtencion.Add(FranjaAtencion.Crear(
                lavadero.Id,
                dia,
                TimeOnly.Parse(apertura),
                TimeOnly.Parse(cierre),
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, exterior.Id, interior.Id, ajeno.Id);
    }

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> LeerErrorAsync(HttpResponseMessage respuesta) =>
        (await LeerAsync(respuesta)).GetProperty("error").GetString();

    [Fact]
    public async Task ElGetDevuelveLaSemanaSembrada()
    {
        var (lavaderoId, _, _, _, _) = await SembrarAsync(
            (1, "08:00", "12:00"),
            (6, "09:00", "18:00"));

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync($"/api/lavaderos/{lavaderoId}/horarios");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = await LeerAsync(respuesta);
        Assert.Equal(lavaderoId, cuerpo.GetProperty("lavaderoId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(cuerpo.GetProperty("zonaHoraria").GetString()));

        var franjas = cuerpo.GetProperty("franjas").EnumerateArray().ToList();
        Assert.Equal(2, franjas.Count);
        Assert.Equal(1, franjas[0].GetProperty("diaSemana").GetInt32());
        Assert.Equal("08:00", franjas[0].GetProperty("apertura").GetString());
        Assert.Equal("12:00", franjas[0].GetProperty("cierre").GetString());
        Assert.Equal(6, franjas[1].GetProperty("diaSemana").GetInt32());
    }

    [Fact]
    public async Task ElPutReemplazaLaSemanaCompleta()
    {
        var (lavaderoId, _, _, _, _) = await SembrarAsync(
            (1, "08:00", "12:00"),
            (2, "08:00", "12:00"));

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/horarios",
            new
            {
                franjas = new[]
                {
                    new { diaSemana = 3, apertura = "10:00", cierre = "14:00" },
                    new { diaSemana = 5, apertura = "16:00", cierre = "20:00" },
                },
            });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = await LeerAsync(respuesta);
        Assert.Equal(2, cuerpo.GetProperty("franjas").GetArrayLength());

        var posteriores = await cliente.GetAsync($"/api/lavaderos/{lavaderoId}/horarios");
        var franjas = (await LeerAsync(posteriores)).GetProperty("franjas").EnumerateArray().ToList();

        Assert.Equal(2, franjas.Count);
        Assert.Equal(3, franjas[0].GetProperty("diaSemana").GetInt32());
        Assert.Equal("10:00", franjas[0].GetProperty("apertura").GetString());
        Assert.Equal(5, franjas[1].GetProperty("diaSemana").GetInt32());
    }

    [Fact]
    public async Task ElPutConFranjasSuperpuestasDevuelve400()
    {
        var (lavaderoId, _, _, _, _) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/horarios",
            new
            {
                franjas = new[]
                {
                    new { diaSemana = 1, apertura = "08:00", cierre = "12:00" },
                    new { diaSemana = 1, apertura = "11:00", cierre = "13:00" },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("franja_invalida", await LeerErrorAsync(respuesta));
    }

    [Fact]
    public async Task ElPutSinFranjasDevuelve400()
    {
        var (lavaderoId, _, _, _, _) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/horarios",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("franja_invalida", await LeerErrorAsync(respuesta));
    }

    [Fact]
    public async Task ElPutDeUnLavaderoInexistenteDevuelve404()
    {
        var inexistente = Guid.NewGuid();

        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(inexistente);

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{inexistente}/horarios",
            new
            {
                franjas = new[]
                {
                    new { diaSemana = 1, apertura = "08:00", cierre = "20:00" },
                },
            });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("lavadero_no_encontrado", await LeerErrorAsync(respuesta));
    }

    [Fact]
    public async Task ElPutSinTokenDevuelve401()
    {
        var (lavaderoId, _, _, _, _) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CrearClienteSinToken();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/horarios",
            new
            {
                franjas = new[]
                {
                    new { diaSemana = 1, apertura = "08:00", cierre = "20:00" },
                },
            });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElCatalogoPublicaVacioCuandoElPuestoAtiendeTodosLosServicios()
    {
        var (lavaderoId, puestoId, _, _, _) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync($"/api/lavaderos/{lavaderoId}/catalogo");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var puesto = (await LeerAsync(respuesta))
            .GetProperty("puestos")
            .EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == puestoId);

        Assert.Empty(puesto.GetProperty("tiposLavadoIds").EnumerateArray());
    }

    [Fact]
    public async Task ElPutDeTiposLavadoAsignaYElCatalogoLoPublica()
    {
        var (lavaderoId, puestoId, exteriorId, _, _) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/tipos-lavado",
            new { tiposLavadoIds = new[] { exteriorId } });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var cuerpo = await LeerAsync(respuesta);
        Assert.Equal(puestoId, cuerpo.GetProperty("puestoId").GetGuid());
        Assert.Equal(
            new[] { exteriorId },
            cuerpo.GetProperty("tiposLavadoIds").EnumerateArray().Select(t => t.GetGuid()));

        var catalogo = await cliente.GetAsync($"/api/lavaderos/{lavaderoId}/catalogo");
        var puesto = (await LeerAsync(catalogo))
            .GetProperty("puestos")
            .EnumerateArray()
            .Single(p => p.GetProperty("id").GetGuid() == puestoId);

        Assert.Equal(
            new[] { exteriorId },
            puesto.GetProperty("tiposLavadoIds").EnumerateArray().Select(t => t.GetGuid()));
    }

    [Fact]
    public async Task ElPutDeTiposLavadoConUnTipoAjenoDevuelve404()
    {
        var (lavaderoId, puestoId, _, _, _) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/tipos-lavado",
            new { tiposLavadoIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("tipo_lavado_no_encontrado", await LeerErrorAsync(respuesta));
    }

    [Fact]
    public async Task UnAdministradorDeOtroLavaderoRecibe403()
    {
        var (lavaderoId, puestoId, exteriorId, _, ajenoId) = await SembrarAsync();

        using var api = CrearApi();
        using var cliente = api.CrearClienteAdmin(ajenoId);

        var respuesta = await cliente.PutAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/tipos-lavado",
            new { tiposLavadoIds = new[] { exteriorId } });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }
}
