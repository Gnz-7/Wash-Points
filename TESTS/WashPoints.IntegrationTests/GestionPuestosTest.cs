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
/// T-44: gestión de puestos de lavado (CU-05) end-to-end sobre HTTP.
///
/// CA-S4.1 — crear un puesto lo deja activo y aparece en el listado.
/// CA-S4.2 — desactivar un puesto sin turnos lo deja inactivo.
/// CA-S4.3/D5 — desactivar un puesto con un turno futuro vigente da 409 y no
///              cambia `activo`.
/// CA-S4.4 — activar lo vuelve a habilitar.
/// CA-S4.5 — nombre duplicado en el mismo lavadero da 409 `puesto_ya_existe`.
/// CA-S4.16 — un puesto de otro lavadero es indistinguible de inexistente (404).
///
/// Ver SPECS/slice-04-agenda-y-puestos.md T-44 y §5.1.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class GestionPuestosTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public GestionPuestosTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private WebApplicationFactory<Program> CrearApi() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion));

    private async Task<Guid> SembrarLavaderoAsync()
    {
        await using var contexto = CrearContexto();
        var lavadero = Lavadero.Crear("Lavadero Central");
        contexto.Lavaderos.Add(lavadero);
        await contexto.SaveChangesAsync();
        return lavadero.Id;
    }

    private async Task<Guid> SembrarPuestoAsync(Guid lavaderoId, string nombre = "Puesto 1")
    {
        await using var contexto = CrearContexto();
        var puesto = Puesto.Crear(lavaderoId, nombre);
        contexto.Puestos.Add(puesto);
        await contexto.SaveChangesAsync();
        return puesto.Id;
    }

    /// <summary>Siembra un turno confirmado que todavía no terminó (futuro).</summary>
    private async Task SembrarTurnoConfirmadoFuturoAsync(Guid lavaderoId, Guid puestoId)
    {
        await using var contexto = CrearContexto();

        var tipo = TipoLavado.Crear(lavaderoId, "Exterior", 30, 15000m, 3000m);
        contexto.TiposLavado.Add(tipo);

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse("2027-06-15T10:00:00"));
        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipo.Id,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
    }

    private async Task<bool> PuestoActivoAsync(Guid puestoId) =>
        await _baseDeDatos.ConsultarEscalarAsync<bool>($"SELECT activo FROM puestos WHERE id = '{puestoId}'");

    private static object CuerpoPuesto(string nombre) => new { nombre };

    [Fact]
    public async Task CrearPuestoQuedaActivo()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto Nuevo"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<RespuestaPuesto>();
        Assert.NotNull(creado);
        Assert.True(creado!.activo);
        Assert.Equal("Puesto Nuevo", creado.nombre);

        var listado = await cliente.GetFromJsonAsync<List<RespuestaPuesto>>(
            $"/api/lavaderos/{lavaderoId}/puestos");
        Assert.NotNull(listado);
        Assert.Contains(listado!, p => p.id == creado.id);
    }

    [Fact]
    public async Task DesactivarPuestoSinTurnosLoInactiva()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var puestoId = await SembrarPuestoAsync(lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/desactivar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(await PuestoActivoAsync(puestoId));
    }

    [Fact]
    public async Task ReactivarPuestoLoHabilita()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var puestoId = await SembrarPuestoAsync(lavaderoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        await cliente.PostAsync($"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/desactivar", null);
        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/activar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(await PuestoActivoAsync(puestoId));
    }

    [Fact]
    public async Task DesactivarPuestoConTurnoFuturoDevuelve409()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        var puestoId = await SembrarPuestoAsync(lavaderoId);
        await SembrarTurnoConfirmadoFuturoAsync(lavaderoId, puestoId);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/desactivar", null);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("puesto_con_turnos_activos", error!.error);
        Assert.True(await PuestoActivoAsync(puestoId));
    }

    [Fact]
    public async Task NombreDuplicadoDevuelve409()
    {
        var lavaderoId = await SembrarLavaderoAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var primero = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto 1"));
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/puestos", CuerpoPuesto("Puesto 1"));

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var error = await segundo.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("puesto_ya_existe", error!.error);
    }

    [Fact]
    public async Task PuestoDeOtroLavaderoDevuelve404()
    {
        var lavaderoA = await SembrarLavaderoAsync();
        var lavaderoB = await SembrarLavaderoAsync();
        var puestoDeB = await SembrarPuestoAsync(lavaderoB, "Puesto B");
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var desactivar = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoA}/puestos/{puestoDeB}/desactivar", null);
        Assert.Equal(HttpStatusCode.NotFound, desactivar.StatusCode);
        var error = await desactivar.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("puesto_no_encontrado", error!.error);

        var activar = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoA}/puestos/{puestoDeB}/activar", null);
        Assert.Equal(HttpStatusCode.NotFound, activar.StatusCode);
    }

    private sealed record RespuestaPuesto(Guid id, string nombre, bool activo);

    private sealed record RespuestaError(string error);
}
