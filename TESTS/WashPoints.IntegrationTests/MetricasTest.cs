using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
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
/// T-63: métricas y BI (CU-09, US-A08) end-to-end sobre HTTP.
///
/// CA-S6.1 — la capacidad refleja los puestos reales (total y activos).
/// CA-S6.2 — señas, facturado total y facturado de mostrador viajan separados.
/// CA-S6.3 — las señas solo cuentan pagos approved del día local.
/// CA-S6.4 — un día sin actividad devuelve 200 con ceros.
/// CA-S6.5 — un administrador ajeno recibe 403; sin sesión, 401.
/// CA-S6.6 — la tasa de ocupación se recorta a la ventana declarada.
///
/// Ver SPECS/slice-06-metricas-bi.md T-63 y §5.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class MetricasTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    /// <summary>Día local usado por los escenarios deterministas.</summary>
    private static readonly DateOnly Dia = new(2026, 10, 10);

    private readonly ContenedorPostgres _baseDeDatos;

    public MetricasTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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
                // Los workers tickean solos; métricas es solo lectura y no
                // depende de ellos, pero el test queda determinista igual.
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

    // ---------------------------------------------------------------- siembra

    private sealed record Catalogo(Guid LavaderoId, Guid TipoId, IReadOnlyList<Guid> Puestos);

    private async Task<Catalogo> SembrarCatalogoAsync(
        string nombre,
        int puestos = 1,
        int puestosActivos = 1)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear(nombre);
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);

        var creados = new List<Puesto>();
        for (var i = 0; i < puestos; i++)
        {
            creados.Add(Puesto.Crear(lavadero.Id, $"Puesto {i + 1}"));
        }

        // Los primeros `puestosActivos` quedan activos; el resto se desactiva.
        for (var i = puestosActivos; i < creados.Count; i++)
        {
            creados[i].Desactivar();
        }

        contexto.Add(lavadero);
        contexto.Add(tipo);
        contexto.AddRange(creados);
        await contexto.SaveChangesAsync();

        return new Catalogo(lavadero.Id, tipo.Id, creados.Select(p => p.Id).ToArray());
    }

    /// <summary>
    /// Siembra un turno con hora <b>local</b> del lavadero y lo lleva al estado
    /// pedido por la máquina de estados real (nunca por asignación directa).
    /// </summary>
    private async Task<Guid> SembrarTurnoAsync(
        Catalogo catalogo,
        Guid puestoId,
        DateTime inicioLocal,
        EstadoTurno estado,
        decimal precioSena = 3000m,
        bool espontaneo = false)
    {
        await using var contexto = CrearContexto();

        var inicioUtc = ConversorHorario.AUtc(inicioLocal);
        var finUtc = inicioUtc.AddMinutes(30);

        var turno = espontaneo
            ? Turno.CrearEspontaneo(
                catalogo.LavaderoId, puestoId, Guid.NewGuid(), catalogo.TipoId, inicioUtc, finUtc)
            : Turno.Crear(
                catalogo.LavaderoId, puestoId, Guid.NewGuid(), catalogo.TipoId,
                inicioUtc, finUtc, precioSena);

        LlevarAEstado(turno, estado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno.Id;
    }

    /// <summary>
    /// Siembra un turno que ocupa el puesto <b>ahora</b> (la capacidad es una
    /// foto del presente, no del día consultado).
    /// </summary>
    private async Task<Guid> SembrarTurnoVigenteAsync(Catalogo catalogo, Guid puestoId)
    {
        await using var contexto = CrearContexto();

        var ahora = DateTime.UtcNow;
        var turno = Turno.Crear(
            catalogo.LavaderoId, puestoId, Guid.NewGuid(), catalogo.TipoId,
            ahora.AddMinutes(-10), ahora.AddMinutes(20), precioSena: 3000m);

        turno.TransicionarA(EstadoTurno.Confirmado);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno.Id;
    }

    private static void LlevarAEstado(Turno turno, EstadoTurno estado)
    {
        switch (estado)
        {
            case EstadoTurno.PendientePago:
                return;
            case EstadoTurno.Confirmado:
                turno.TransicionarA(EstadoTurno.Confirmado);
                return;
            case EstadoTurno.EnCurso:
                AsegurarEnCurso(turno);
                return;
            case EstadoTurno.Finalizado:
                AsegurarEnCurso(turno);
                turno.TransicionarA(EstadoTurno.Finalizado);
                return;
            case EstadoTurno.NoPresentado:
                turno.TransicionarA(EstadoTurno.Confirmado);
                turno.TransicionarA(EstadoTurno.NoPresentado);
                return;
            case EstadoTurno.Cancelado:
                if (turno.Estado == EstadoTurno.PendientePago)
                {
                    turno.TransicionarA(EstadoTurno.Cancelado);
                    return;
                }

                turno.TransicionarA(EstadoTurno.Confirmado);
                turno.TransicionarA(EstadoTurno.Cancelado);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(estado));
        }
    }

    /// <summary>
    /// Deja el turno en <c>en_curso</c>. El espontáneo ya nace ahí (no pasa por
    /// <c>pendiente_pago</c>), así que solo transiciona quien viene de reserva.
    /// </summary>
    private static void AsegurarEnCurso(Turno turno)
    {
        if (turno.Estado == EstadoTurno.PendientePago)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
        }

        if (turno.Estado == EstadoTurno.Confirmado)
        {
            turno.TransicionarA(EstadoTurno.EnCurso);
        }
    }

    /// <summary>
    /// Inserta el evento de pago con su instante de procesamiento explícito.
    /// <see cref="WashPoints.Infrastructure.Pagos.Pago.Registrar"/> fija
    /// <c>ahora</c>, y estos escenarios necesitan un día determinista.
    /// </summary>
    private async Task SembrarPagoAsync(
        Guid turnoId,
        string mpPaymentId,
        string resultado,
        DateTime procesadoEnUtc)
    {
        var instante = procesadoEnUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await _baseDeDatos.EjecutarSqlAsync($"""
            INSERT INTO pagos (id, turno_id, mp_payment_id, tipo_evento, resultado, procesado_en)
            VALUES ('{Guid.NewGuid()}', '{turnoId}', '{mpPaymentId}', 'payment', '{resultado}', '{instante}');
            """);
    }

    private static DateTime Local(int hora, int minuto = 0) =>
        new(Dia.Year, Dia.Month, Dia.Day, hora, minuto, 0);

    private static DateTime Utc(int hora, int minuto = 0) =>
        new(Dia.Year, Dia.Month, Dia.Day, hora, minuto, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------- capacidad

    [Fact]
    public async Task CapacidadReflejaLosPuestosReales()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Capacidad", puestos: 3, puestosActivos: 2);
        await SembrarTurnoVigenteAsync(catalogo, catalogo.Puestos[0]);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var resumen = await cliente.GetFromJsonAsync<RespuestaResumen>(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha={Dia:yyyy-MM-dd}");

        Assert.NotNull(resumen);
        Assert.Equal(3, resumen!.Capacidad.PuestosTotal);
        Assert.Equal(2, resumen.Capacidad.PuestosActivos);
        Assert.Equal(1, resumen.Capacidad.PuestosOcupados);
        Assert.NotEqual(default, resumen.Capacidad.CalculadaEn);
    }

    // --------------------------------------------------------------- señas

    [Fact]
    public async Task SoloLasSenasAprobadasDelDiaCuentan()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Senas", puestos: 3);

        var aprobado = await SembrarTurnoAsync(catalogo, catalogo.Puestos[0], Local(9), EstadoTurno.Confirmado);
        var rechazado = await SembrarTurnoAsync(catalogo, catalogo.Puestos[1], Local(10), EstadoTurno.PendientePago);
        var pendiente = await SembrarTurnoAsync(catalogo, catalogo.Puestos[2], Local(11), EstadoTurno.PendientePago);

        await SembrarPagoAsync(aprobado, "mp-aprobado", "approved", Utc(12));
        await SembrarPagoAsync(rechazado, "mp-rechazado", "rejected", Utc(13));
        await SembrarPagoAsync(pendiente, "mp-pendiente", "pending", Utc(14));

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var resumen = await cliente.GetFromJsonAsync<RespuestaResumen>(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha={Dia:yyyy-MM-dd}");

        Assert.NotNull(resumen);
        // Solo el pago approved del día suma su seña (3000), aunque el turno
        // siga confirmado y no se haya entregado el servicio.
        Assert.Equal(3000m, resumen!.Ingresos.SenasCobradas);
        Assert.Equal(0m, resumen.Ingresos.FacturadoTotal);
        Assert.Equal(0m, resumen.Ingresos.FacturadoMostrador);
        Assert.Equal("ARS", resumen.Ingresos.Moneda);
    }

    [Fact]
    public async Task ElEspontaneoSumaAMostradorYNoASenas()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Mostrador");
        await SembrarTurnoAsync(
            catalogo, catalogo.Puestos[0], Local(9), EstadoTurno.Finalizado, espontaneo: true);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var resumen = await cliente.GetFromJsonAsync<RespuestaResumen>(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha={Dia:yyyy-MM-dd}");

        Assert.NotNull(resumen);
        // El espontáneo nace sin seña: cobra en mostrador, no adelanta dinero.
        Assert.Equal(0m, resumen!.Ingresos.SenasCobradas);
        Assert.Equal(15000m, resumen.Ingresos.FacturadoTotal);
        Assert.Equal(15000m, resumen.Ingresos.FacturadoMostrador);
        Assert.Equal(1, resumen.Turnos.Atendidos);
        Assert.Equal(1, resumen.Turnos.Espontaneos);
    }

    [Fact]
    public async Task LosNoPresentadosYCanceladosSeCuentan()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Conteos", puestos: 2);
        await SembrarTurnoAsync(catalogo, catalogo.Puestos[0], Local(9), EstadoTurno.NoPresentado);
        await SembrarTurnoAsync(catalogo, catalogo.Puestos[1], Local(10), EstadoTurno.Cancelado);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var resumen = await cliente.GetFromJsonAsync<RespuestaResumen>(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha={Dia:yyyy-MM-dd}");

        Assert.NotNull(resumen);
        Assert.Equal(1, resumen!.Turnos.NoPresentados);
        Assert.Equal(1, resumen.Turnos.Cancelaciones);
        Assert.Equal(0, resumen.Turnos.Atendidos);
        Assert.Equal(0m, resumen.Ingresos.FacturadoTotal);
    }

    [Fact]
    public async Task UnDiaSinActividadDevuelveCeros()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Vacío", puestos: 2);

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha=2026-10-11");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var resumen = await respuesta.Content.ReadFromJsonAsync<RespuestaResumen>();
        Assert.NotNull(resumen);
        Assert.Equal(0m, resumen!.Ingresos.SenasCobradas);
        Assert.Equal(0m, resumen.Ingresos.FacturadoTotal);
        Assert.Equal(0m, resumen.Ingresos.FacturadoMostrador);
        Assert.Equal(0, resumen.Turnos.Atendidos);
        Assert.Equal(0, resumen.Turnos.Espontaneos);
        Assert.Equal(0, resumen.Turnos.NoPresentados);
        Assert.Equal(0, resumen.Turnos.Cancelaciones);
        // La capacidad es una foto del presente: el lavadero tiene sus puestos.
        Assert.Equal(2, resumen.Capacidad.PuestosTotal);
    }

    // ------------------------------------------------------------ seguridad

    [Fact]
    public async Task UnAdministradorAjenoNoVeElResumen()
    {
        var catalogoA = await SembrarCatalogoAsync("Lavadero A");
        var catalogoB = await SembrarCatalogoAsync("Lavadero B");

        using var api = CrearApi();
        // Sesión de administrador de B pidiendo las métricas de A.
        using var cliente = api.CrearClienteAdmin(catalogoB.LavaderoId);

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{catalogoA.LavaderoId}/metricas/resumen?fecha={Dia:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task SinSesionDevuelve401()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Sin Sesión");

        using var api = CrearApi();
        using var cliente = api.CrearClienteSinToken();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha={Dia:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task UnaFechaInvalidaDevuelve400()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Fecha Inválida");

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/resumen?fecha=2026-13-40");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.NotNull(error);
        Assert.Equal("fecha_invalida", error!.Error);
    }

    // ------------------------------------------------------------- ocupación

    [Fact]
    public async Task LaOcupacionDevuelveTodosLosPuestosConSuTasa()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Ocupación", puestos: 2);

        // 03:10Z–03:40Z = 30 minutos ocupados dentro de una ventana de 60.
        var inicioUtc = Utc(3, 10);
        await using (var contexto = CrearContexto())
        {
            var turno = Turno.Crear(
                catalogo.LavaderoId, catalogo.Puestos[0], Guid.NewGuid(), catalogo.TipoId,
                inicioUtc, inicioUtc.AddMinutes(30), precioSena: 3000m);
            turno.TransicionarA(EstadoTurno.Confirmado);
            contexto.Turnos.Add(turno);
            await contexto.SaveChangesAsync();
        }

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var ocupacion = await cliente.GetFromJsonAsync<RespuestaOcupacion>(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/ocupacion" +
            $"?fecha={Dia:yyyy-MM-dd}&desde=2026-10-10T03:00:00Z&hasta=2026-10-10T04:00:00Z");

        Assert.NotNull(ocupacion);
        Assert.Equal(2, ocupacion!.Puestos.Count);

        var ocupado = ocupacion.Puestos.Single(p => p.PuestoId == catalogo.Puestos[0]);
        Assert.Equal(30, ocupado.MinutosOcupados);
        Assert.Equal(60, ocupado.MinutosVentana);
        Assert.Equal(0.5m, ocupado.Tasa);

        var libre = ocupacion.Puestos.Single(p => p.PuestoId == catalogo.Puestos[1]);
        Assert.Equal(0, libre.MinutosOcupados);
        Assert.Equal(0m, libre.Tasa);
    }

    [Fact]
    public async Task UnaVentanaInvalidaDevuelve400()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Ventana Inválida");

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{catalogo.LavaderoId}/metricas/ocupacion" +
            "?desde=2026-10-10T04:00:00Z&hasta=2026-10-10T03:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.NotNull(error);
        Assert.Equal("ventana_invalida", error!.Error);
    }

    [Fact]
    public async Task UnPuestoAjenoDevuelve404()
    {
        var catalogoA = await SembrarCatalogoAsync("Lavadero Propio");
        var catalogoB = await SembrarCatalogoAsync("Lavadero Ajeno");

        using var api = CrearApi();
        using var cliente = api.CreateClient();

        var respuesta = await cliente.GetAsync(
            $"/api/lavaderos/{catalogoA.LavaderoId}/metricas/ocupacion" +
            $"?fecha={Dia:yyyy-MM-dd}&puestoId={catalogoB.Puestos[0]}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);

        var error = await respuesta.Content.ReadFromJsonAsync<RespuestaError>();
        Assert.NotNull(error);
        Assert.Equal("puesto_no_encontrado", error!.Error);
    }

    // ---------------------------------------------------------------- DTOs

    private sealed record RespuestaResumen(
        string Fecha,
        RespuestaCapacidad Capacidad,
        RespuestaIngresos Ingresos,
        RespuestaTurnos Turnos);

    private sealed record RespuestaCapacidad(
        int PuestosTotal,
        int PuestosActivos,
        int PuestosOcupados,
        DateTime CalculadaEn);

    private sealed record RespuestaIngresos(
        decimal SenasCobradas,
        decimal FacturadoTotal,
        decimal FacturadoMostrador,
        string Moneda);

    private sealed record RespuestaTurnos(
        int Atendidos,
        int Espontaneos,
        int NoPresentados,
        int Cancelaciones);

    private sealed record RespuestaOcupacion(
        DateTime Desde,
        DateTime Hasta,
        List<RespuestaPuesto> Puestos);

    private sealed record RespuestaPuesto(
        Guid PuestoId,
        string Nombre,
        int MinutosOcupados,
        int MinutosVentana,
        decimal Tasa);

    private sealed record RespuestaError(string Error);
}
