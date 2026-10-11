using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using WashPoints.UnitTests.Fakes;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-15: el endpoint de reserva y la disponibilidad con el pendiente vigente.
///
/// CA-S1.1 — la reserva crea un <c>pendiente_pago</c> vigente que bloquea; la
/// disponibilidad lo muestra ocupado.
/// CA-1.2 / CA-S1.3 — mapeo de errores de la pasarela y de la reserva.
/// CA-S1.9 — un slot ya tomado responde 409.
///
/// Ver SPECS/slice-01-reserva-sena-mp.md §5 (T-15) y §6.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class EndpointReservaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public EndpointReservaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE pagos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId, Guid ClienteId)> SembrarAsync()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id, cliente.Id);
    }

    private WebApplicationFactory<Program> CrearApi(PasarelaPagosFakeSimulada pasarela) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(pasarela);
            });
        });

    private static object Cuerpo(Guid puestoId, Guid tipoId, Guid clienteId, string inicioLocal) => new
    {
        puestoId,
        tipoLavadoId = tipoId,
        clienteId,
        inicio = inicioLocal,
    };

    [Fact]
    public async Task ReservarDevuelve201ConElTurnoPendiente()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, "2026-10-10T10:00:00"));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.Content.ReadFromJsonAsync<RespuestaTurno>();
        Assert.NotNull(creado);
        Assert.Equal("pendiente_pago", creado!.estado);
        Assert.False(string.IsNullOrWhiteSpace(creado.mpPreferenceId));
    }

    [Fact]
    public async Task LaDisponibilidadMuestraElPendienteVigenteComoOcupado()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var alta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, "2026-10-10T10:00:00"));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var ocupados = await cliente.GetFromJsonAsync<List<RespuestaDisponibilidad>>(
            $"/api/lavaderos/{lavaderoId}/disponibilidad?desde=2026-10-10T09:00:00&hasta=2026-10-10T12:00:00");

        Assert.NotNull(ocupados);
        var intervalo = Assert.Single(ocupados!);
        Assert.Equal("2026-10-10T10:00:00", intervalo.desde);
    }

    [Fact]
    public async Task UnSlotYaTomadoDevuelve409()
    {
        var (lavaderoId, puestoId, tipoId, _) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var primero = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, Guid.NewGuid(), "2026-10-10T10:00:00"));
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);

        var segundo = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, Guid.NewGuid(), "2026-10-10T10:00:00"));

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    }

    [Fact]
    public async Task UnRequestInvalidoDevuelve400()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        var pasarela = new PasarelaPagosFakeSimulada();
        using var api = CrearApi(pasarela);
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, ""));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    private sealed record RespuestaTurno(
        Guid id,
        string estado,
        string? mpPreferenceId,
        decimal precioSena);

    private sealed record RespuestaDisponibilidad(
        Guid turnoId,
        Guid puestoId,
        string origen,
        string desde,
        string hasta);
}
