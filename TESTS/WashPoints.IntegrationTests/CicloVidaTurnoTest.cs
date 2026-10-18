using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-46: ciclo de vida del turno desde la agenda (CU-06, US-A06).
///
/// CA-S4.9 — `en_curso → finalizado` libera el puesto (deja de figurar en disponibilidad).
/// CA-S4.10 — una transición no permitida ⇒ 409 `turno_transicion_invalida`.
/// CA-S4.12 — `no_presentado` por acción manual libera el puesto.
/// CA-S4.13 — `confirmado → cancelado` libera el puesto y habilita el D5.
/// CA-S4.16 — un turno de otro lavadero es indistinguible de inexistente (404).
///
/// Ver SPECS/slice-04-agenda-y-puestos.md T-46 y §5.1.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CicloVidaTurnoTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public CicloVidaTurnoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId)> SembrarCatalogoAsync(string nombre)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear(nombre);
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    private async Task<Guid> SembrarTurnoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoId, string inicioLocal, EstadoTurno estado)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(DateTime.Parse(inicioLocal));
        var turno = Turno.Crear(
            lavaderoId, puestoId, Guid.NewGuid(), tipoId,
            inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);

        if (estado is EstadoTurno.Confirmado or EstadoTurno.EnCurso)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
        }

        if (estado == EstadoTurno.EnCurso)
        {
            turno.TransicionarA(EstadoTurno.EnCurso);
        }

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno.Id;
    }

    [Fact]
    public async Task FinalizarPoneFinalizadoYLiberaPuesto()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Finalizar");
        var turnoId = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T10:00:00", EstadoTurno.EnCurso);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/turnos/{turnoId}/finalizar", null);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaTransicion>();
        Assert.Equal("finalizado", cuerpo!.estado);

        var disponibilidad = await cliente.GetFromJsonAsync<List<RespuestaDisponibilidad>>(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T09:00:00&hasta=2026-10-10T12:00:00");
        Assert.NotNull(disponibilidad);
        Assert.DoesNotContain(disponibilidad!, o => o.turnoId == turnoId);
    }

    [Fact]
    public async Task FinalizarTurnoNoEnCursoDevuelve409()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Invalida");
        var turnoId = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2026-10-10T10:00:00", EstadoTurno.Confirmado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/turnos/{turnoId}/finalizar", null);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("turno_transicion_invalida", error!.error);
    }

    [Fact]
    public async Task CancelarConfirmadoLiberaYHabilitaD5()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero Cancelar");
        var turnoId = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2027-06-15T10:00:00", EstadoTurno.Confirmado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var bloqueado = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/desactivar", null);
        Assert.Equal(HttpStatusCode.Conflict, bloqueado.StatusCode);

        var cancelar = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/turnos/{turnoId}/cancelar", null);
        Assert.Equal(HttpStatusCode.OK, cancelar.StatusCode);
        var cuerpo = await cancelar.Content.ReadFromJsonAsync<RespuestaTransicion>();
        Assert.Equal("cancelado", cuerpo!.estado);

        var liberado = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/desactivar", null);
        Assert.Equal(HttpStatusCode.OK, liberado.StatusCode);
    }

    [Fact]
    public async Task NoPresentadoManualLiberaPuesto()
    {
        var (lavaderoId, puestoId, tipoId) = await SembrarCatalogoAsync("Lavadero NoPresentado");
        var turnoId = await SembrarTurnoAsync(lavaderoId, puestoId, tipoId, "2027-06-15T10:00:00", EstadoTurno.Confirmado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/turnos/{turnoId}/no-presentado", null);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaTransicion>();
        Assert.Equal("no_presentado", cuerpo!.estado);

        var liberado = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoId}/puestos/{puestoId}/desactivar", null);
        Assert.Equal(HttpStatusCode.OK, liberado.StatusCode);
    }

    [Fact]
    public async Task FinalizarTurnoDeOtroLavaderoDevuelve404()
    {
        var (lavaderoA, _, _) = await SembrarCatalogoAsync("Lavadero A");
        var (lavaderoB, puestoB, tipoB) = await SembrarCatalogoAsync("Lavadero B");
        var turnoDeB = await SembrarTurnoAsync(lavaderoB, puestoB, tipoB, "2026-10-10T10:00:00", EstadoTurno.EnCurso);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsync(
            $"/api/lavaderos/{lavaderoA}/turnos/{turnoDeB}/finalizar", null);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("turno_no_encontrado", error!.error);
    }

    private sealed record RespuestaTransicion(Guid turnoId, string estado);

    private sealed record RespuestaDisponibilidad(Guid turnoId, Guid puestoId, string origen);

    private sealed record RespuestaError(string error);
}
