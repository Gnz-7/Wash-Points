using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.Application.Ocupacion;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Test del servicio de ocupación contra Postgres real, sobre la migración.
/// Todo pasa por <c>IOcupacionServicio</c>: es la única puerta de escritura
/// sobre `turnos` (RB-2, ARCHITECTURE §2.2 módulo Ocupacion).
///
/// Ver SPECS/slice-00-nucleo-turnos.md T-05.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class OcupacionServicioTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public OcupacionServicioTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private WashPointsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options;
        return new WashPointsDbContext(opciones);
    }

    private OcupacionServicio CrearServicio(WashPointsDbContext contexto) =>
        new(new OcupacionRepositorio(contexto));

    private async Task LimpiarTablas()
    {
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);
    }

    /// <summary>
    /// Siembra el catálogo mínimo y devuelve el contexto + servicio listos
    /// para operar. Un contexto por servicion: refleja un "request HTTP".
    /// </summary>
    private async Task<Semilla> SembrarCatalogo()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        return new Semilla(lavadero.Id, puesto.Id, tipo.Id, cliente.Id);
    }

    private static Turno TurnoEn(Semilla s, DateTime inicioUtc, DateTime finUtc) =>
        Turno.Crear(
            lavaderoId: s.LavaderoId,
            puestoId: s.PuestoId,
            clienteId: s.ClienteId,
            tipoLavadoId: s.TipoLavadoId,
            inicioUtc: inicioUtc,
            finUtc: finUtc,
            precioSena: 3000m);

    [Fact]
    public async Task ReservarBloqueaElIntervalo()
    {
        // Slice-01 (reescritura de T-22, CA-S1.1): el pendiente_pago vigente
        // BLOQUEA. La segunda reserva del mismo puesto+rango choca contra el
        // predicado de 3 estados del EXCLUDE (23P01 -> TurnoConflictoException).
        var s = await SembrarCatalogo();
        await using var contexto = CrearContexto();
        var servicio = CrearServicio(contexto);

        var inicioUtc = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var a = TurnoEn(s, inicioUtc, inicioUtc.AddHours(1));
        var b = TurnoEn(s, inicioUtc, inicioUtc.AddHours(1));

        await servicio.ReservarAsync(a, CancellationToken.None);

        await Assert.ThrowsAsync<TurnoConflictoException>(
            () => servicio.ReservarAsync(b, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmarDosTurnosAdyacentesNoChoca()
    {
        // CA-S0.3 (US-C04 CA-4.3). [10,11) y [11,12) contiguos: el segundo no
        // solapa el primero y ambos pueden confirmarse.
        var s = await SembrarCatalogo();
        await using var contexto = CrearContexto();
        var servicio = CrearServicio(contexto);

        var matutino = TurnoEn(s,
            new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc));
        var idMatutino = await servicio.ReservarAsync(matutino, CancellationToken.None);

        var contiguo = TurnoEn(s,
            new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc));
        var idContiguo = await servicio.ReservarAsync(contiguo, CancellationToken.None);

        await servicio.ConfirmarOcupacionAsync(idMatutino, CancellationToken.None);
        await servicio.ConfirmarOcupacionAsync(idContiguo, CancellationToken.None);

        Assert.True(matutino.OcupaPuesto);
        Assert.True(contiguo.OcupaPuesto);
    }

    [Fact]
    public async Task ReservarSolapadoConOtroConfirmadoLanzaConflicto()
    {
        // Slice-01 (reescritura de T-22): el conflicto se decide al RESERVAR, no
        // al confirmar. Un pendiente que solapa un turno ya confirmado choca
        // contra la EXCLUDE en el INSERT (=> 409 en la capa HTTP).
        var s = await SembrarCatalogo();
        await using var contexto = CrearContexto();
        var servicio = CrearServicio(contexto);

        var primero = TurnoEn(s,
            new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc));
        var idPrimero = await servicio.ReservarAsync(primero, CancellationToken.None);
        await servicio.ConfirmarOcupacionAsync(idPrimero, CancellationToken.None);

        var solapado = TurnoEn(s,
            new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 10, 16, 0, 0, DateTimeKind.Utc));

        await Assert.ThrowsAsync<TurnoConflictoException>(
            () => servicio.ReservarAsync(solapado, CancellationToken.None));
    }

    private sealed record Semilla(
        Guid LavaderoId,
        Guid PuestoId,
        Guid TipoLavadoId,
        Guid ClienteId);
}
