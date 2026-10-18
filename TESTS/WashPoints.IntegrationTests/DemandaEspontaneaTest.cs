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
/// T-36: demanda espontánea (CU-07) y su integración con la disponibilidad y la
/// outbox.
///
/// CA-S3.1 — la espontánea crea un turno `en_curso` con `origen='espontanea'`.
/// CA-S3.2 — el solapamiento con un turno vigente da 409 sin crear.
/// CA-S3.5 — no exige seña: no se crea fila en `pagos`.
/// CA-S3.6 — la disponibilidad integra `app` y `espontanea` por igual.
/// CA-S3.7 — la entrada inválida da 400 y no persiste nada.
///
/// Ver SPECS/slice-03-sincronizacion-rb2.md §5 (T-36) y §6.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class DemandaEspontaneaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public DemandaEspontaneaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId)> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    /// <summary>Siembra un turno confirmado (de app) que ya ocupa el intervalo.</summary>
    private async Task SembrarTurnoConfirmadoAsync(Guid lavaderoId, Guid puestoId, Guid tipoId, string inicioLocal)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse(inicioLocal));
        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
    }

    private CustomWebApplicationFactory CrearApi() =>
        new(_baseDeDatos.CadenaConexion, builder =>
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion));

    private static object Cuerpo(Guid puestoId, Guid tipoId, string inicioLocal) => new
    {
        puestoId,
        tipoLavadoId = tipoId,
        inicio = inicioLocal,
    };

    private async Task<int> ContarFilasAsync(string tabla)
    {
        var total = await _baseDeDatos.ConsultarEscalarAsync<long>($"SELECT COUNT(*) FROM {tabla}");
        return (int)total;
    }

    [Fact]
    public async Task DemandaEspontaneaCreaTurnoYDisponibilidadLaVe()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarAsync();
        await SembrarTurnoConfirmadoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T08:00:00");
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            Cuerpo(puestoId, tipoId, "2026-10-10T10:00:00"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<RespuestaEspontanea>();
        Assert.NotNull(creado);
        Assert.Equal("en_curso", creado!.estado);

        var origen = await _baseDeDatos.ConsultarEscalarAsync<string>(
            $"SELECT origen FROM turnos WHERE id = '{creado.id}'");
        Assert.Equal("espontanea", origen);

        var ocupados = await cliente.GetFromJsonAsync<List<RespuestaDisponibilidad>>(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T07:00:00&hasta=2026-10-10T11:00:00");

        Assert.NotNull(ocupados);
        var origenes = ocupados!.Select(o => o.origen).ToHashSet();
        Assert.Contains("app", origenes);
        Assert.Contains("espontanea", origenes);
        Assert.Contains(ocupados, o => o.desde == "2026-10-10T10:00:00");
    }

    [Fact]
    public async Task DemandaEspontaneaSobrePuestoOcupadoDevuelve409YNoCrea()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarAsync();
        await SembrarTurnoConfirmadoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T10:00:00");
        var antes = await ContarFilasAsync("turnos");
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            Cuerpo(puestoId, tipoId, "2026-10-10T10:00:00"));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("slot_ya_ocupado", error!.error);
        Assert.Equal(antes, await ContarFilasAsync("turnos"));
    }

    [Fact]
    public async Task DemandaEspontaneaNoCreaPago()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            Cuerpo(puestoId, tipoId, "2026-10-10T10:00:00"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(0, await ContarFilasAsync("pagos"));
    }

    [Fact]
    public async Task DemandaEspontaneaConDatosInvalidosDevuelve400()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var sinPuesto = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            Cuerpo(Guid.Empty, tipoId, "2026-10-10T10:00:00"));
        Assert.Equal(HttpStatusCode.BadRequest, sinPuesto.StatusCode);

        var inicioInvalido = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos-espontaneos",
            Cuerpo(puestoId, tipoId, "no-es-una-fecha"));
        Assert.Equal(HttpStatusCode.BadRequest, inicioInvalido.StatusCode);

        Assert.Equal(0, await ContarFilasAsync("turnos"));
    }

    private sealed record RespuestaEspontanea(Guid id, string estado, Guid puestoId, string desde, string hasta);

    private sealed record RespuestaDisponibilidad(Guid turnoId, Guid puestoId, string origen, string desde, string hasta);

    private sealed record RespuestaError(string error);
}
