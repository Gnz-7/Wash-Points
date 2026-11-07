using Microsoft.EntityFrameworkCore;
using WashPoints.Application.Reembolsos;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Puertos;
using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Pagos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-155/T-156: reclamo atómico con <c>FOR UPDATE SKIP LOCKED</c>, lease,
/// clasificación del servicio y persistencia del reintegro contra PostgreSQL
/// real. Ver SPECS/slice-15-pagos-reembolsos.md D-15.4 y T-155…T-157.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ReembolsosEjecucionTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private static readonly DateTime Ahora = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private readonly ContenedorPostgres _baseDeDatos;

    public ReembolsosEjecucionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private async Task LimpiarTablas() =>
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE outbox, comprobantes, pagos, reembolsos, turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options);

    // ---------------------------------------------------------------- siembra

    private sealed record Catalogo(Guid LavaderoId, Guid TipoId, Guid PuestoId);

    private async Task<Catalogo> SembrarCatalogoAsync(string nombre)
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear(nombre);
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");

        contexto.Add(lavadero);
        contexto.Add(tipo);
        contexto.Add(puesto);
        await contexto.SaveChangesAsync();

        return new Catalogo(lavadero.Id, tipo.Id, puesto.Id);
    }

    private async Task<Turno> SembrarTurnoAsync(Catalogo catalogo, DateTime inicioUtc)
    {
        await using var contexto = CrearContexto();

        var turno = Turno.Crear(
            lavaderoId: catalogo.LavaderoId,
            puestoId: catalogo.PuestoId,
            clienteId: Guid.NewGuid(),
            tipoLavadoId: catalogo.TipoId,
            inicioUtc: inicioUtc,
            finUtc: inicioUtc.AddMinutes(30),
            precioSena: 3000m,
            precioTotal: 15000m);

        turno.TransicionarA(EstadoTurno.Confirmado);
        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno;
    }

    private async Task AgregarPagoAprobadoAsync(Guid turnoId, string mpPaymentId)
    {
        await using var contexto = CrearContexto();
        contexto.Pagos.Add(Pago.Registrar(turnoId, mpPaymentId, TipoEventoPago.Payment, ResultadoPago.Approved));
        await contexto.SaveChangesAsync();
    }

    private async Task<Reembolso> SembrarReembolsoAsync(
        Guid turnoId,
        EstadoReembolso estado = EstadoReembolso.Pendiente,
        MotivoReembolso motivo = MotivoReembolso.ClienteEnPlazo,
        DateTime? proximoIntentoEn = null,
        DateTime? creadoEn = null)
    {
        var reembolso = Reembolso.Registrar(
            turnoId,
            estado,
            motivo,
            3000m,
            creadoEn ?? Ahora.AddHours(-1));

        if (proximoIntentoEn is not null)
        {
            reembolso.ProgramarReintento(proximoIntentoEn.Value, "programado");
        }

        await using var contexto = CrearContexto();
        contexto.Reembolsos.Add(reembolso);
        await contexto.SaveChangesAsync();
        return reembolso;
    }

    private async Task<Reembolso?> LeerReembolsoAsync(Guid turnoId)
    {
        await using var contexto = CrearContexto();
        return await new ReembolsoRepositorio(contexto)
            .ObtenerPorTurnoAsync(turnoId, CancellationToken.None);
    }

    // ------------------------------------------------------------- reclamo

    [Fact]
    public async Task ElReclamoSoloTomaPendientesVencidosYSumaUnIntento()
    {
        await LimpiarTablas();
        var catalogo = await SembrarCatalogoAsync("Lavadero Reclamo");

        var vencido = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));
        var futuro = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 14, 0, 0, DateTimeKind.Utc));
        var sinPago = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc));

        var reembolsoVencido = await SembrarReembolsoAsync(vencido.Id);
        await SembrarReembolsoAsync(
            futuro.Id,
            proximoIntentoEn: Ahora.AddHours(1));
        await SembrarReembolsoAsync(
            sinPago.Id,
            estado: EstadoReembolso.NoAplica,
            motivo: MotivoReembolso.SinPago);

        await using var contexto = CrearContexto();
        var reclamados = await new ReembolsoRepositorio(contexto)
            .ReclamarPendientesAsync(10, Ahora, Lease, CancellationToken.None);

        var unico = Assert.Single(reclamados);
        Assert.Equal(reembolsoVencido.Id, unico.Id);
        Assert.Equal(1, unico.Intentos);
        Assert.Equal(Ahora, unico.ReclamadoEn);
    }

    [Fact]
    public async Task ElLeaseImpideQueDosTicksTomenLaMismaFila()
    {
        await LimpiarTablas();
        var catalogo = await SembrarCatalogoAsync("Lavadero Lease");
        var turno = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));
        await SembrarReembolsoAsync(turno.Id);

        await using (var primero = CrearContexto())
        {
            var tomados = await new ReembolsoRepositorio(primero)
                .ReclamarPendientesAsync(10, Ahora, Lease, CancellationToken.None);
            Assert.Single(tomados);
        }

        await using (var segundo = CrearContexto())
        {
            var tomados = await new ReembolsoRepositorio(segundo)
                .ReclamarPendientesAsync(10, Ahora.AddMinutes(1), Lease, CancellationToken.None);
            Assert.Empty(tomados);
        }

        await using (var tercero = CrearContexto())
        {
            var tomados = await new ReembolsoRepositorio(tercero)
                .ReclamarPendientesAsync(10, Ahora.AddMinutes(6), Lease, CancellationToken.None);
            var unico = Assert.Single(tomados);
            Assert.Equal(2, unico.Intentos);
        }
    }

    [Fact]
    public async Task UnReembolsoNoAplicaNuncaSeReclama()
    {
        await LimpiarTablas();
        var catalogo = await SembrarCatalogoAsync("Lavadero Sin Pago");
        var turno = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));
        await SembrarReembolsoAsync(
            turno.Id,
            estado: EstadoReembolso.NoAplica,
            motivo: MotivoReembolso.SinPago);

        await using var contexto = CrearContexto();
        var reclamados = await new ReembolsoRepositorio(contexto)
            .ReclamarPendientesAsync(10, Ahora.AddDays(1), Lease, CancellationToken.None);

        Assert.Empty(reclamados);
    }

    [Fact]
    public async Task ObtenerPagoAprobadoDevuelveElPagoDelTurno()
    {
        await LimpiarTablas();
        var catalogo = await SembrarCatalogoAsync("Lavadero Pago");
        var turno = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));
        await AgregarPagoAprobadoAsync(turno.Id, "77");

        await using var contexto = CrearContexto();
        var pagoId = await new ReembolsoRepositorio(contexto)
            .ObtenerPagoAprobadoAsync(turno.Id, CancellationToken.None);

        Assert.Equal("77", pagoId);
    }

    // --------------------------------------------------------------- tick

    [Fact]
    public async Task ElTickProcesaUnReembolsoYPersisteElReintegro()
    {
        await LimpiarTablas();
        var catalogo = await SembrarCatalogoAsync("Lavadero Tick");
        var turno = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));
        await AgregarPagoAprobadoAsync(turno.Id, "77");
        await SembrarReembolsoAsync(turno.Id);

        var pasarela = new PasarelaFalsa(ResultadoReembolso.Aprobado("900"));

        await using (var contexto = CrearContexto())
        {
            var servicio = new ProcesarReembolsosServicio(
                new ReembolsoRepositorio(contexto),
                pasarela,
                new OpcionesReembolso(),
                () => Ahora,
                (_, _) => 0);

            var resultado = await servicio.EjecutarTickAsync(CancellationToken.None);

            Assert.Equal(1, resultado.Procesados);
            Assert.Equal("77", pasarela.PagoReembolsado);
            Assert.Equal(3000m, pasarela.MontoReembolsado);
        }

        var guardado = await LeerReembolsoAsync(turno.Id);
        Assert.NotNull(guardado);
        Assert.Equal(EstadoReembolso.Procesado, guardado!.Estado);
        Assert.Equal("900", guardado.MpRefundId);
        Assert.NotNull(guardado.ProcesadoEn);
        Assert.Null(guardado.ReclamadoEn);
        Assert.Null(guardado.ProximoIntentoEn);
        Assert.Equal(1, guardado.Intentos);
    }

    [Fact]
    public async Task ElTickSinPagoAprobadoMarcaFallidoSinLlamarALaPasarela()
    {
        await LimpiarTablas();
        var catalogo = await SembrarCatalogoAsync("Lavadero Sin Pago Tick");
        var turno = await SembrarTurnoAsync(catalogo, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));
        await SembrarReembolsoAsync(turno.Id);

        var pasarela = new PasarelaFalsa(ResultadoReembolso.Aprobado("900"));

        await using (var contexto = CrearContexto())
        {
            var servicio = new ProcesarReembolsosServicio(
                new ReembolsoRepositorio(contexto),
                pasarela,
                new OpcionesReembolso(),
                () => Ahora,
                (_, _) => 0);

            var resultado = await servicio.EjecutarTickAsync(CancellationToken.None);

            Assert.Equal(1, resultado.Fallidos);
            Assert.Equal(0, pasarela.Llamadas);
        }

        var guardado = await LeerReembolsoAsync(turno.Id);
        Assert.NotNull(guardado);
        Assert.Equal(EstadoReembolso.Fallido, guardado!.Estado);
        Assert.Contains("no tiene un pago aprobado", guardado.UltimoError);
    }

    private sealed class PasarelaFalsa(ResultadoReembolso respuesta) : IPasarelaPagos
    {
        public int Llamadas { get; private set; }
        public string? PagoReembolsado { get; private set; }
        public decimal MontoReembolsado { get; private set; }

        public Task<PreferenciaCreada> CrearPreferenciaAsync(
            PreferenciaPago preferencia, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente());

        public Task<ResultadoVerificacionPago> BuscarPagoPorTurnoAsync(Guid turnoId, CancellationToken ct) =>
            Task.FromResult(ResultadoVerificacionPago.Pendiente(turnoId));

        public Task<ResultadoReembolso> ReembolsarAsync(
            string pagoId, decimal monto, string? claveIdempotencia, CancellationToken ct)
        {
            Llamadas++;
            PagoReembolsado = pagoId;
            MontoReembolsado = monto;
            return Task.FromResult(respuesta);
        }

        public Task<ResultadoReembolso> ConsultarReembolsoAsync(
            string pagoId, string reembolsoId, CancellationToken ct) =>
            Task.FromResult(respuesta);
    }
}
