using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Ciclo del cliente en HTTP: «Mis turnos» y cancelación con reembolso
/// (SPECS/slice-14, T-144).
///
/// Protege RB-1 y RB-3: el cliente sólo ve y cancela sus propios turnos, y la
/// cancelación deja registrado el reembolso que ordena la política. Un
/// administrador autenticado no puede usar ninguno de los dos endpoints.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class MisTurnosTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    /// <summary>Un turno futuro y lejano: siempre cae dentro del plazo de reembolso.</summary>
    private static readonly DateTime Futuro = new(2099, 6, 10, 13, 0, 0, DateTimeKind.Utc);

    /// <summary>Un turno del pasado: siempre cae fuera del plazo.</summary>
    private static readonly DateTime Pasado = new(2020, 1, 10, 13, 0, 0, DateTimeKind.Utc);

    private readonly ContenedorPostgres _baseDeDatos;

    public MisTurnosTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, reembolsos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
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

    private sealed record Catalogo(Guid LavaderoId, Guid TipoId, Guid PuestoId);

    private async Task<Catalogo> SembrarCatalogoAsync(string nombre = "Lavadero Centro")
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear(nombre);
        var tipo = TipoLavado.Crear(lavadero.Id, "Lavado completo", 30, 15000m, 3000m);
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");

        contexto.Lavaderos.Add(lavadero);
        contexto.TiposLavado.Add(tipo);
        contexto.Puestos.Add(puesto);
        await contexto.SaveChangesAsync();

        return new Catalogo(lavadero.Id, tipo.Id, puesto.Id);
    }

    private async Task<Turno> SembrarTurnoAsync(
        Catalogo catalogo,
        Guid clienteId,
        DateTime inicioUtc,
        EstadoTurno estado = EstadoTurno.Confirmado)
    {
        await using var contexto = CrearContexto();

        var turno = Turno.Crear(
            lavaderoId: catalogo.LavaderoId,
            puestoId: catalogo.PuestoId,
            clienteId: clienteId,
            tipoLavadoId: catalogo.TipoId,
            inicioUtc: inicioUtc,
            finUtc: inicioUtc.AddMinutes(30),
            precioSena: 3000m,
            precioTotal: 15000m);

        if (estado is EstadoTurno.Confirmado or EstadoTurno.EnCurso or EstadoTurno.Finalizado
            or EstadoTurno.NoPresentado or EstadoTurno.Cancelado)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
        }

        if (estado is EstadoTurno.EnCurso or EstadoTurno.Finalizado)
        {
            turno.TransicionarA(EstadoTurno.EnCurso);
        }

        if (estado is EstadoTurno.Finalizado)
        {
            turno.TransicionarA(EstadoTurno.Finalizado);
        }

        if (estado is EstadoTurno.NoPresentado)
        {
            turno.TransicionarA(EstadoTurno.NoPresentado);
        }

        if (estado is EstadoTurno.Cancelado)
        {
            turno.CancelarPorAdministrador();
        }

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();

        return turno;
    }

    private sealed record ReembolsoDto(string Estado, decimal Monto, string Motivo);

    private sealed record TurnoClienteDto(
        Guid TurnoId,
        Guid LavaderoId,
        string LavaderoNombre,
        string? Direccion,
        string TipoLavado,
        int DuracionMinutos,
        DateTime Inicio,
        DateTime Fin,
        string Estado,
        decimal PrecioTotal,
        decimal PrecioSena,
        bool ComprobanteDisponible,
        bool Cancelable,
        decimal ReembolsoEstimado,
        string? ReembolsoMotivo,
        ReembolsoDto? Reembolso);

    private sealed record CancelacionDto(Guid TurnoId, string Estado, ReembolsoDto Reembolso);

    private sealed record RespuestaError(string Error);

    [Fact]
    public async Task UnClienteVeSusTurnosDelMasRecienteAlMasViejo()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();

        await SembrarTurnoAsync(catalogo, clienteId, Futuro.AddHours(-4));
        await SembrarTurnoAsync(catalogo, clienteId, Futuro);
        await SembrarTurnoAsync(catalogo, Guid.NewGuid(), Futuro.AddHours(2));

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var turnos = await cliente.GetFromJsonAsync<TurnoClienteDto[]>("/api/turnos/mios");

        Assert.NotNull(turnos);
        Assert.Equal(2, turnos!.Length);
        Assert.Equal(Futuro, turnos[0].Inicio);
        Assert.Equal(Futuro.AddHours(-4), turnos[1].Inicio);
        Assert.All(turnos, turno => Assert.Equal(catalogo.LavaderoId, turno.LavaderoId));
        Assert.All(turnos, turno => Assert.Equal("Lavadero Centro", turno.LavaderoNombre));
        Assert.All(turnos, turno => Assert.Equal("Lavado completo", turno.TipoLavado));
        Assert.All(turnos, turno => Assert.Equal(30, turno.DuracionMinutos));
        Assert.All(turnos, turno => Assert.Equal(15000m, turno.PrecioTotal));
        Assert.All(turnos, turno => Assert.Equal(3000m, turno.PrecioSena));
        Assert.All(turnos, turno => Assert.Equal("confirmado", turno.Estado));
        Assert.All(turnos, turno => Assert.True(turno.Cancelable));
        Assert.All(turnos, turno => Assert.Equal(3000m, turno.ReembolsoEstimado));
        Assert.All(turnos, turno => Assert.Equal("cliente_en_plazo", turno.ReembolsoMotivo));
        Assert.All(turnos, turno => Assert.Null(turno.Reembolso));
    }

    [Fact]
    public async Task UnTurnoFueraDePlazoSePuedeCancelarPeroNoGeneraReintegro()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();
        await SembrarTurnoAsync(catalogo, clienteId, Pasado);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var turnos = await cliente.GetFromJsonAsync<TurnoClienteDto[]>("/api/turnos/mios");

        Assert.NotNull(turnos);
        var turno = Assert.Single(turnos!);
        Assert.True(turno.Cancelable);
        Assert.Equal(0m, turno.ReembolsoEstimado);
        Assert.Equal("cliente_fuera_de_plazo", turno.ReembolsoMotivo);
    }

    [Fact]
    public async Task UnTurnoEnCursoNoEsCancelableNiMuestraComprobante()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();
        await SembrarTurnoAsync(catalogo, clienteId, Futuro, EstadoTurno.EnCurso);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var turnos = await cliente.GetFromJsonAsync<TurnoClienteDto[]>("/api/turnos/mios");

        Assert.NotNull(turnos);
        var turno = Assert.Single(turnos!);
        Assert.False(turno.Cancelable);
        Assert.False(turno.ComprobanteDisponible);
        Assert.Equal(0m, turno.ReembolsoEstimado);
    }

    [Fact]
    public async Task UnClienteSinTurnosRecibeUnaListaVacia()
    {
        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(Guid.NewGuid());

        var turnos = await cliente.GetFromJsonAsync<TurnoClienteDto[]>("/api/turnos/mios");

        Assert.NotNull(turnos);
        Assert.Empty(turnos!);
    }

    [Fact]
    public async Task UnAdministradorNoPuedeVerMisTurnos()
    {
        var catalogo = await SembrarCatalogoAsync();

        using var api = CrearApi();
        using var administrador = api.CrearClienteAdmin(catalogo.LavaderoId);

        var respuesta = await administrador.GetAsync("/api/turnos/mios");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task SinSesionMisTurnosDevuelveNoAutorizado()
    {
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.GetAsync("/api/turnos/mios");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElClienteCancelaSuTurnoYSeRegistraElReembolsoPendiente()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();
        var turno = await SembrarTurnoAsync(catalogo, clienteId, Futuro);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var respuesta = await cliente.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cancelacion = await respuesta.Content.ReadFromJsonAsync<CancelacionDto>();
        Assert.NotNull(cancelacion);
        Assert.Equal(turno.Id, cancelacion!.TurnoId);
        Assert.Equal("cancelado", cancelacion.Estado);
        Assert.Equal("pendiente", cancelacion.Reembolso.Estado);
        Assert.Equal(3000m, cancelacion.Reembolso.Monto);
        Assert.Equal("cliente_en_plazo", cancelacion.Reembolso.Motivo);

        await using var contexto = CrearContexto();
        var reembolso = await contexto.Reembolsos.SingleAsync(r => r.TurnoId == turno.Id);
        Assert.Equal("pendiente", reembolso.Estado.ToString().ToLowerInvariant() == "pendiente" ? "pendiente" : "otro");
    }

    [Fact]
    public async Task CancelarUnTurnoDeOtroClienteDevuelveNoEncontrado()
    {
        var catalogo = await SembrarCatalogoAsync();
        var turno = await SembrarTurnoAsync(catalogo, Guid.NewGuid(), Futuro);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(Guid.NewGuid());

        var respuesta = await cliente.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("turno_no_encontrado", error?.Error);
    }

    [Fact]
    public async Task UnTurnoEnCursoNoSePuedeCancelar()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();
        var turno = await SembrarTurnoAsync(catalogo, clienteId, Futuro, EstadoTurno.EnCurso);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var respuesta = await cliente.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("turno_transicion_invalida", error?.Error);
    }

    [Fact]
    public async Task LaSegundaCancelacionDevuelveConflicto()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();
        var turno = await SembrarTurnoAsync(catalogo, clienteId, Futuro);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var primera = await cliente.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        var segunda = await cliente.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);

        await using var contexto = CrearContexto();
        Assert.Equal(1, await contexto.Reembolsos.CountAsync(r => r.TurnoId == turno.Id));
    }

    [Fact]
    public async Task UnAdministradorNoPuedeUsarLaCancelacionDelCliente()
    {
        var catalogo = await SembrarCatalogoAsync();
        var turno = await SembrarTurnoAsync(catalogo, Guid.NewGuid(), Futuro);

        using var api = CrearApi();
        using var administrador = api.CrearClienteAdmin(catalogo.LavaderoId);

        var respuesta = await administrador.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task UnTurnoPendienteDePagoSeCancelaSinReintegro()
    {
        var catalogo = await SembrarCatalogoAsync();
        var clienteId = Guid.NewGuid();
        var turno = await SembrarTurnoAsync(catalogo, clienteId, Futuro, EstadoTurno.PendientePago);

        using var api = CrearApi();
        using var cliente = api.CrearClienteDeCliente(clienteId);

        var respuesta = await cliente.PostAsync($"/api/turnos/{turno.Id}/cancelar", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cancelacion = await respuesta.Content.ReadFromJsonAsync<CancelacionDto>();
        Assert.NotNull(cancelacion);
        Assert.Equal("no_aplica", cancelacion!.Reembolso.Estado);
        Assert.Equal(0m, cancelacion.Reembolso.Monto);
        Assert.Equal("sin_pago", cancelacion.Reembolso.Motivo);
    }
}
