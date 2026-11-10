using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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
/// T-185: revalidación de las reglas temporales en la reserva end-to-end
/// (SPECS/slice-17 §4.4, D-17.5, D-17.10).
///
/// CA-17.3 — un lavadero sin franjas rechaza cualquier reserva con
///           <c>422 fuera_de_horario_atencion</c>.
/// CA-17.5 — una reserva que cae íntegra dentro de una franja se acepta.
/// CA-17.7 — sin la anticipación mínima responde <c>422 anticipacion_insuficiente</c>.
/// CA-17.9 — más allá del horizonte responde <c>422 fuera_de_horizonte</c>.
/// CA-17.10 — en el pasado responde <c>422 anticipacion_insuficiente</c>.
/// CA-17.11 / CA-17.12 — un tipo o un puesto de otro lavadero responden 404.
/// CA-17.13 / CA-17.14 — la compatibilidad puesto↔servicio se aplica en la reserva.
///
/// El orden de validación es el de D-17.5: pertenencia → ventana temporal →
/// horario → compatibilidad. Todos los rechazos ocurren antes de crear el turno
/// y de pedir la preferencia de pago (RB-1).
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ReglasReservaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public ReglasReservaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    /// <summary>Día de los casos: mañana en hora local.</summary>
    private static readonly DateOnly Dia = DateOnly.FromDateTime(DateTime.Now).AddDays(1);

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE pagos, turnos, clientes, puestos, tipos_lavado,
                           horarios_atencion, puestos_tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    /// <summary>
    /// Siembra un lavadero con un puesto y un tipo de 30 minutos, un cliente y,
    /// salvo que se pida lo contrario, la franja 08:00–20:00 del día de los
    /// casos. <paramref name="tiposDelPuesto"/> agrega la compatibilidad
    /// explícita del puesto (lista vacía = atiende todos, D-17.7).
    /// </summary>
    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoId, Guid ClienteId)>
        SembrarAsync(bool abrir = true, Guid[]? tiposDelPuesto = null)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);

        if (abrir)
        {
            contexto.HorariosAtencion.Add(FranjaAtencion.Crear(
                lavadero.Id,
                DiaSemana(Dia),
                new TimeOnly(8, 0),
                new TimeOnly(20, 0),
                DateTime.UtcNow));
        }

        foreach (var tipoId in tiposDelPuesto ?? Array.Empty<Guid>())
        {
            contexto.PuestosTiposLavado.Add(PuestoTipoLavado.Crear(puesto.Id, tipoId));
        }

        await contexto.SaveChangesAsync();

        return (lavadero.Id, puesto.Id, tipo.Id, cliente.Id);
    }

    /// <summary>Día ISO-8601 del calendario de atención (domingo = 7).</summary>
    private static int DiaSemana(DateOnly fecha) =>
        fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek;

    private static string Inicio(DateOnly fecha, int hora, int minuto = 0) =>
        $"{fecha:yyyy-MM-dd}T{hora:00}:{minuto:00}:00";

    /// <summary>
    /// API con la pasarela falsa. <paramref name="anticipacionMinimaMinutos"/>
    /// permite mover la ventana temporal para probar el rechazo sin depender de
    /// la hora a la que corre la suite.
    /// </summary>
    private CustomWebApplicationFactory CrearApi(
        PasarelaPagosFakeSimulada pasarela,
        int? anticipacionMinimaMinutos = null) =>
        new(_baseDeDatos.CadenaConexion, builder =>
        {
            builder.UseSetting("ConnectionStrings:Postgres", _baseDeDatos.CadenaConexion);
            if (anticipacionMinimaMinutos is int minutos)
            {
                builder.UseSetting("Agenda:AnticipacionMinimaMinutos", minutos.ToString());
            }

            builder.ConfigureServices(servicios =>
            {
                servicios.RemoveAll<IPasarelaPagos>();
                servicios.AddSingleton<IPasarelaPagos>(pasarela);
            });
        });

    private static object Cuerpo(Guid puestoId, Guid tipoId, Guid clienteId, string inicio) => new
    {
        puestoId,
        tipoLavadoId = tipoId,
        clienteId,
        inicio,
    };

    private static async Task<RespuestaError> LeerErrorAsync(HttpResponseMessage respuesta)
    {
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.NotNull(error);
        return error!;
    }

    [Fact]
    public async Task UnDiaSinFranjasRechazaLaReserva()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync(abrir: false);
        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia, 10)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("fuera_de_horario_atencion", error.error);
    }

    [Fact]
    public async Task UnaReservaDentroDeLaFranjaSeAcepta()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia, 10)));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    [Fact]
    public async Task UnaReservaSinLaAnticipacionMinimaSeRechaza()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        // Una anticipación mayor que la distancia al turno fuerza el rechazo
        // sin depender de la hora a la que corre la suite.
        using var api = CrearApi(new PasarelaPagosFakeSimulada(), anticipacionMinimaMinutos: 100_000);
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia, 10)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("anticipacion_insuficiente", error.error);
    }

    [Fact]
    public async Task UnaReservaMasAllaDelHorizonteSeRechaza()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia.AddDays(8), 10)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("fuera_de_horizonte", error.error);
    }

    [Fact]
    public async Task UnaReservaEnElPasadoSeRechaza()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia.AddDays(-1), 10)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("anticipacion_insuficiente", error.error);
    }

    [Fact]
    public async Task UnTipoDeOtroLavaderoDevuelve404()
    {
        var (lavaderoId, puestoId, _, clienteId) = await SembrarAsync();

        Guid tipoAjeno;
        await using (var contexto = CrearContexto())
        {
            var otro = Lavadero.Crear("Otro Lavadero");
            var tipo = TipoLavado.Crear(otro.Id, "Ajeno", 30, 10000m, 2000m);
            contexto.AddRange(otro, tipo);
            await contexto.SaveChangesAsync();
            tipoAjeno = tipo.Id;
        }

        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoAjeno, clienteId, Inicio(Dia, 10)));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("tipo_lavado_no_encontrado", error.error);
    }

    [Fact]
    public async Task UnPuestoDeOtroLavaderoDevuelve404()
    {
        var (lavaderoId, _, tipoId, clienteId) = await SembrarAsync();

        Guid puestoAjeno;
        await using (var contexto = CrearContexto())
        {
            var otro = Lavadero.Crear("Otro Lavadero");
            var puesto = Puesto.Crear(otro.Id, "Puesto ajeno");
            contexto.AddRange(otro, puesto);
            await contexto.SaveChangesAsync();
            puestoAjeno = puesto.Id;
        }

        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoAjeno, tipoId, clienteId, Inicio(Dia, 10)));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("puesto_no_encontrado", error.error);
    }

    [Fact]
    public async Task UnPuestoConFilasQueNoIncluyeElTipoDevuelve422()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();

        await using (var contexto = CrearContexto())
        {
            var otroTipo = TipoLavado.Crear(lavaderoId, "Interior", 30, 12000m, 2500m);
            contexto.TiposLavado.Add(otroTipo);
            // El puesto declara que sólo atiende el otro tipo.
            contexto.PuestosTiposLavado.Add(PuestoTipoLavado.Crear(puestoId, otroTipo.Id));
            await contexto.SaveChangesAsync();
        }

        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia, 10)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await LeerErrorAsync(respuesta);
        Assert.Equal("puesto_incompatible", error.error);
    }

    [Fact]
    public async Task UnPuestoSinFilasAceptaCualquierTipoActivo()
    {
        var (lavaderoId, puestoId, tipoId, clienteId) = await SembrarAsync();
        using var api = CrearApi(new PasarelaPagosFakeSimulada());
        using var cliente = api.CreateClient();

        var respuesta = await cliente.PostAsJsonAsync(
            $"/api/lavaderos/{lavaderoId}/turnos",
            Cuerpo(puestoId, tipoId, clienteId, Inicio(Dia, 11)));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    private sealed record RespuestaError(string error, string? detalle);
}
