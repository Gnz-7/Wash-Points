using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Reembolsos;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-143: persistencia de reembolsos y listado de turnos por cliente.
///
/// Cubre lo que el slice de pagos va a necesitar: que el reembolso se guarde
/// con su monto congelado, que un turno no pueda tener dos reembolsos, que la
/// autoría de la cancelación se persista como texto y que «Mis turnos» reciba
/// los turnos del cliente ordenados del más reciente al más viejo.
///
/// Ver SPECS/slice-14-ciclo-cliente-cancelacion.md §5.2 y T-143.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ReembolsoPersistenciaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public ReembolsoPersistenciaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

        if (estado == EstadoTurno.Confirmado)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
        }
        else if (estado == EstadoTurno.Cancelado)
        {
            turno.TransicionarA(EstadoTurno.Confirmado);
            turno.CancelarPorCliente();
        }

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();
        return turno;
    }

    // ------------------------------------------------------------ reembolsos

    [Fact]
    public async Task UnReembolsoSeGuardaConSuMontoYSeLeePorTurno()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Reembolso");
        var turno = await SembrarTurnoAsync(
            catalogo, Guid.NewGuid(), new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));

        var reembolso = Reembolso.Registrar(
            turno.Id,
            EstadoReembolso.Pendiente,
            MotivoReembolso.ClienteEnPlazo,
            3000m,
            new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc));

        await using (var contexto = CrearContexto())
        {
            var repositorio = new ReembolsoRepositorio(contexto);
            await repositorio.AgregarAsync(reembolso, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using var lectura = CrearContexto();
        var guardado = await new ReembolsoRepositorio(lectura)
            .ObtenerPorTurnoAsync(turno.Id, CancellationToken.None);

        Assert.NotNull(guardado);
        Assert.Equal(EstadoReembolso.Pendiente, guardado!.Estado);
        Assert.Equal(MotivoReembolso.ClienteEnPlazo, guardado.Motivo);
        Assert.Equal(3000m, guardado.Monto);
        Assert.Null(guardado.ProcesadoEn);
    }

    [Fact]
    public async Task UnTurnoNoPuedeTenerDosReembolsos()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Reembolso Único");
        var turno = await SembrarTurnoAsync(
            catalogo, Guid.NewGuid(), new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));

        var primero = Reembolso.Registrar(
            turno.Id, EstadoReembolso.Pendiente, MotivoReembolso.ClienteEnPlazo, 3000m, DateTime.UtcNow);
        var segundo = Reembolso.Registrar(
            turno.Id, EstadoReembolso.NoAplica, MotivoReembolso.ClienteFueraDePlazo, 0m, DateTime.UtcNow);

        await using (var contexto = CrearContexto())
        {
            var repositorio = new ReembolsoRepositorio(contexto);
            await repositorio.AgregarAsync(primero, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        await using (var contexto = CrearContexto())
        {
            var repositorio = new ReembolsoRepositorio(contexto);
            await repositorio.AgregarAsync(segundo, CancellationToken.None);

            await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task UnReembolsoSinAplicarSeGuardaConMontoCero()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Sin Reintegro");
        var turno = await SembrarTurnoAsync(
            catalogo, Guid.NewGuid(), new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));

        var reembolso = Reembolso.Registrar(
            turno.Id, EstadoReembolso.NoAplica, MotivoReembolso.SinPago, 0m, DateTime.UtcNow);

        await using (var contexto = CrearContexto())
        {
            var repositorio = new ReembolsoRepositorio(contexto);
            await repositorio.AgregarAsync(reembolso, CancellationToken.None);
            await contexto.SaveChangesAsync();
        }

        var monto = await _baseDeDatos.ConsultarEscalarAsync<decimal>(
            $"SELECT monto FROM reembolsos WHERE turno_id = '{turno.Id}';");
        var estado = await _baseDeDatos.ConsultarEscalarAsync<string>(
            $"SELECT estado FROM reembolsos WHERE turno_id = '{turno.Id}';");

        Assert.Equal(0m, monto);
        Assert.Equal("no_aplica", estado);
    }

    // ------------------------------------------------------- autoría y listado

    [Fact]
    public async Task LaAutoriaDeLaCancelacionSePersisteEnSnakeCase()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Autoría");
        var turno = await SembrarTurnoAsync(
            catalogo,
            Guid.NewGuid(),
            new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc),
            EstadoTurno.Cancelado);

        var autor = await _baseDeDatos.ConsultarEscalarAsync<string>(
            $"SELECT cancelado_por FROM turnos WHERE id = '{turno.Id}';");

        Assert.Equal("cliente", autor);
    }

    [Fact]
    public async Task ElListadoPorClienteDevuelveSoloSusTurnosDelMasRecienteAlMasViejo()
    {
        var catalogo = await SembrarCatalogoAsync("Lavadero Historial");
        var clienteId = Guid.NewGuid();
        var otroClienteId = Guid.NewGuid();

        var antiguo = await SembrarTurnoAsync(
            catalogo, clienteId, new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc));
        var reciente = await SembrarTurnoAsync(
            catalogo, clienteId, new DateTime(2026, 11, 10, 17, 0, 0, DateTimeKind.Utc));
        await SembrarTurnoAsync(
            catalogo, otroClienteId, new DateTime(2026, 11, 10, 13, 0, 0, DateTimeKind.Utc));

        await using var contexto = CrearContexto();
        var turnos = await new OcupacionRepositorio(contexto)
            .ListarPorClienteAsync(clienteId, CancellationToken.None);

        Assert.Equal(2, turnos.Count);
        Assert.Equal(reciente.Id, turnos[0].Id);
        Assert.Equal(antiguo.Id, turnos[1].Id);
        Assert.All(turnos, turno => Assert.Equal(clienteId, turno.ClienteId));
    }

    [Fact]
    public async Task UnClienteSinTurnosRecibeUnaListaVacia()
    {
        await SembrarCatalogoAsync("Lavadero Sin Turnos");

        await using var contexto = CrearContexto();
        var turnos = await new OcupacionRepositorio(contexto)
            .ListarPorClienteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(turnos);
    }
}
