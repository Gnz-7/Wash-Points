using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Infrastructure.Tiempo;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-185: grilla de slots end-to-end (SPECS/slice-17 §4.3, D-17.9).
///
/// CA-17.19 — la grilla sale del horario de atención del día y de la duración
///            del tipo de lavado, con los slots ocupados y los que caen dentro
///            de la anticipación mínima publicados como no disponibles.
/// CA-17.20 — un turno vigente sobre el único puesto compatible marca el slot
///            como `ocupado` y el siguiente vuelve a estar libre.
/// CA-17.21 — un día sin franjas responde 200 con `abierto: false` y sin slots.
/// CA-17.22 — fuera de la ventana [hoy, hoy + horizonte] responde 422; un
///            lavadero o un tipo de otro lavadero responden 404; una fecha o un
///            tipo mal formados responden 400.
///
/// La fecha consultada siempre es «mañana» en hora local: cae dentro del
/// horizonte y todos sus slots son futuros, así que la anticipación mínima no
/// contamina las aserciones de disponibilidad.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class SlotsTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public SlotsTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private static DateOnly Manana => DateOnly.FromDateTime(DateTime.Now).AddDays(1);

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
    /// Siembra un lavadero con un puesto y un tipo de lavado de
    /// <paramref name="duracionMinutos"/>. Con <paramref name="abrirManana"/> en
    /// true le agrega la franja 08:00–20:00 del día de mañana.
    /// </summary>
    private async Task<(Guid LavaderoId, Guid PuestoId, Guid TipoLavadoId)> SembrarAsync(
        int duracionMinutos = 30, bool abrirManana = true)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", duracionMinutos, 15000m, 3000m);

        contexto.AddRange(lavadero, puesto, tipo);

        if (abrirManana)
        {
            contexto.HorariosAtencion.Add(FranjaAtencion.Crear(
                lavadero.Id,
                DiaIso(Manana),
                new TimeOnly(8, 0),
                new TimeOnly(20, 0),
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        await contexto.SaveChangesAsync();
        return (lavadero.Id, puesto.Id, tipo.Id);
    }

    /// <summary>Día ISO-8601 del calendario de atención (domingo = 7).</summary>
    private static int DiaIso(DateOnly fecha) =>
        fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek;

    private async Task SembrarTurnoConfirmadoAsync(
        Guid lavaderoId, Guid puestoId, Guid tipoLavadoId, DateOnly fecha, int hora, int minuto)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(fecha.ToDateTime(new TimeOnly(hora, minuto)));
        var turno = Turno.Crear(
            lavaderoId,
            puestoId,
            Guid.NewGuid(),
            tipoLavadoId,
            inicioUtc,
            inicioUtc.AddMinutes(30),
            precioSena: 3000m,
            precioTotal: 15000m);
        turno.TransicionarA(EstadoTurno.Confirmado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
    }

    private static string Url(Guid lavaderoId, DateOnly fecha, Guid tipoLavadoId) =>
        $"/api/lavaderos/{lavaderoId}/slots?fecha={fecha:yyyy-MM-dd}&tipoLavadoId={tipoLavadoId}";

    [Fact]
    public async Task LaGrillaSaleDelHorarioYDeLaDuracionDelServicio()
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync(duracionMinutos: 30);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(Url(lavaderoId, Manana, tipoLavadoId));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var grilla = await respuesta.Content.ReadFromJsonAsync<RespuestaGrilla>();
        Assert.NotNull(grilla);
        Assert.True(grilla!.abierto);
        Assert.Equal(30, grilla.duracionMinutos);
        Assert.Equal($"{Manana:yyyy-MM-dd}", grilla.fecha);
        Assert.Equal("America/Argentina/Buenos_Aires", grilla.zonaHoraria);
        Assert.Equal(tipoLavadoId, grilla.tipoLavadoId);

        var franja = Assert.Single(grilla.franjas);
        Assert.Equal("08:00", franja.apertura);
        Assert.Equal("20:00", franja.cierre);

        // 12 horas de atención en bloques de 30 minutos.
        Assert.Equal(24, grilla.slots.Count);
        Assert.Equal("08:00", grilla.slots[0].inicio);
        Assert.Equal("08:30", grilla.slots[0].fin);
        Assert.Equal("19:30", grilla.slots[^1].inicio);
        Assert.Equal("20:00", grilla.slots[^1].fin);
        Assert.All(grilla.slots, slot =>
        {
            Assert.True(slot.disponible);
            Assert.Equal(1, slot.puestosLibres);
            Assert.Null(slot.motivo);
        });
    }

    [Fact]
    public async Task DescartaLosSlotsQueNoEntranEnLaFranja()
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync(duracionMinutos: 45);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var grilla = await cliente.GetFromJsonAsync<RespuestaGrilla>(
            Url(lavaderoId, Manana, tipoLavadoId));

        // 08:00–20:00 en bloques de 45 minutos: el último que termina dentro de
        // la franja es 19:15 (19:15 + 45 = 20:00). Un bloque que empiece 19:15
        // es válido; 20:00 no existe como inicio.
        Assert.NotNull(grilla);
        Assert.Equal(16, grilla!.slots.Count);
        Assert.Equal("08:00", grilla.slots[0].inicio);
        Assert.Equal("19:15", grilla.slots[^1].inicio);
        Assert.Equal("20:00", grilla.slots[^1].fin);
    }

    [Fact]
    public async Task UnTurnoVigenteMarcaElSlotComoOcupado()
    {
        var (lavaderoId, puestoId, tipoLavadoId) = await SembrarAsync();
        await SembrarTurnoConfirmadoAsync(lavaderoId, puestoId, tipoLavadoId, Manana, 10, 0);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var grilla = await cliente.GetFromJsonAsync<RespuestaGrilla>(
            Url(lavaderoId, Manana, tipoLavadoId));

        Assert.NotNull(grilla);
        var ocupado = grilla!.slots.Single(s => s.inicio == "10:00");
        Assert.False(ocupado.disponible);
        Assert.Equal(0, ocupado.puestosLibres);
        Assert.Equal("ocupado", ocupado.motivo);

        var siguiente = grilla.slots.Single(s => s.inicio == "10:30");
        Assert.True(siguiente.disponible);
        Assert.Equal(1, siguiente.puestosLibres);
        Assert.Null(siguiente.motivo);

        // El resto de la grilla no se ve afectado por el turno.
        Assert.Equal(23, grilla.slots.Count(s => s.disponible));
    }

    [Fact]
    public async Task UnDiaSinFranjasDevuelveLaGrillaCerrada()
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync(abrirManana: false);
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(Url(lavaderoId, Manana, tipoLavadoId));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var grilla = await respuesta.Content.ReadFromJsonAsync<RespuestaGrilla>();
        Assert.NotNull(grilla);
        Assert.False(grilla!.abierto);
        Assert.Empty(grilla.franjas);
        Assert.Empty(grilla.slots);
    }

    [Fact]
    public async Task UnaFechaFueraDelHorizonteDevuelve422()
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            Url(lavaderoId, DateOnly.FromDateTime(DateTime.Now).AddDays(8), tipoLavadoId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("fuera_de_horizonte", error!.error);
    }

    [Fact]
    public async Task UnaFechaDelPasadoDevuelve422()
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            Url(lavaderoId, DateOnly.FromDateTime(DateTime.Now).AddDays(-1), tipoLavadoId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("fuera_de_horizonte", error!.error);
    }

    [Fact]
    public async Task UnLavaderoInexistenteDevuelve404()
    {
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(Url(Guid.NewGuid(), Manana, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("lavadero_no_encontrado", error!.error);
    }

    [Fact]
    public async Task UnTipoDeOtroLavaderoDevuelve404()
    {
        var (lavaderoId, _, _) = await SembrarAsync();

        Guid tipoAjeno;
        await using (var contexto = CrearContexto())
        {
            var otro = Lavadero.Crear("Otro Lavadero");
            var tipo = TipoLavado.Crear(otro.Id, "Ajeno", 30, 10000m, 2000m);
            contexto.AddRange(otro, tipo);
            await contexto.SaveChangesAsync();
            tipoAjeno = tipo.Id;
        }

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(Url(lavaderoId, Manana, tipoAjeno));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("tipo_lavado_no_encontrado", error!.error);
    }

    [Theory]
    [InlineData("2026-13-40", "fecha_invalida")]
    [InlineData("manana", "fecha_invalida")]
    public async Task UnaFechaMalFormadaDevuelve400(string fecha, string codigo)
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/slots?fecha={fecha}&tipoLavadoId={tipoLavadoId}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal(codigo, error!.error);
    }

    [Theory]
    [InlineData("no-es-un-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task UnTipoMalFormadoDevuelve400(string tipoLavadoId)
    {
        var (lavaderoId, _, _) = await SembrarAsync();
        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{lavaderoId}/slots?fecha={Manana:yyyy-MM-dd}&tipoLavadoId={tipoLavadoId}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.Equal("tipo_lavado_invalido", error!.error);
    }

    [Fact]
    public async Task SinTokenLaGrillaDevuelve401()
    {
        var (lavaderoId, _, tipoLavadoId) = await SembrarAsync();
        using var api = CrearApi();
        using var anonimo = api.CrearClienteSinToken();

        var respuesta = await anonimo.GetAsync(Url(lavaderoId, Manana, tipoLavadoId));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    private sealed record RespuestaGrilla(
        string fecha,
        string zonaHoraria,
        Guid tipoLavadoId,
        int duracionMinutos,
        bool abierto,
        List<RespuestaFranja> franjas,
        List<RespuestaSlot> slots);

    private sealed record RespuestaFranja(string apertura, string cierre);

    private sealed record RespuestaSlot(
        string inicio, string fin, bool disponible, int puestosLibres, string? motivo);

    private sealed record RespuestaError(string error);
}
