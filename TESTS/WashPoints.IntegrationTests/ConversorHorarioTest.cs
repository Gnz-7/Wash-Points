using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-09: la conversión de huso (ARCHITECTURE §10.6) contra Postgres real.
///
/// El dominio guarda UTC; la columna <c>rango</c> guarda hora local sin zona; y
/// los timestamps quedan en UTC puro. Ver SPECS/slice-00-nucleo-turnos.md T-09.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class ConversorHorarioTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public ConversorHorarioTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private WashPointsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options;
        return new WashPointsDbContext(opciones);
    }

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

    private async Task LimpiarTablas()
    {
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);
    }

    [Fact]
    public async Task UnTurnoDeLasTreceUtcSePersisteComoLasDiezLocales()
    {
        // Dirección dominio → base. 13:00 UTC es 10:00 en Buenos Aires (UTC-3,
        // sin cambio de horario desde 2009). La columna tsrange guarda hora local
        // SIN zona: se lee como local, no como instante.
        var s = await SembrarCatalogo();

        var turno = Turno.Crear(
            lavaderoId: s.LavaderoId,
            puestoId: s.PuestoId,
            clienteId: s.ClienteId,
            tipoLavadoId: s.TipoLavadoId,
            inicioUtc: new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            finUtc: new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            precioSena: 3000m);

        await using (var contexto = CrearContexto())
        {
            contexto.Turnos.Add(turno);
            await contexto.SaveChangesAsync();
        }

        // Lectura cruda de la columna: Npgsql la devuelve como el texto que
        // Postgres guardó ("2026-10-10 10:00:00"), no el instante UTC.
        var rangoGuardado = await _baseDeDatos.ConsultarEscalarAsync<string>(
            $"SELECT rango::text FROM turnos WHERE id = '{turno.Id}'");

        Assert.Contains("2026-10-10 10:00", rangoGuardado);
        Assert.Contains("2026-10-10 11:00", rangoGuardado);
        Assert.DoesNotContain("13:00", rangoGuardado);
        Assert.DoesNotContain("14:00", rangoGuardado);
    }

    [Fact]
    public async Task LosTimestampsDelTurnoSeGuardanYLeenEnUtc()
    {
        // Dirección "cualquier fecha → UTC": las columnas timestamptz guardan
        // instantes, y el cliente EF las materializa como DateTime.Kind.Utc.
        var s = await SembrarCatalogo();

        var turno = Turno.Crear(
            lavaderoId: s.LavaderoId,
            puestoId: s.PuestoId,
            clienteId: s.ClienteId,
            tipoLavadoId: s.TipoLavadoId,
            inicioUtc: new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc),
            finUtc: new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc),
            precioSena: 3000m);
        turno.TransicionarA(EstadoTurno.Confirmado);

        await using (var contexto = CrearContexto())
        {
            contexto.Turnos.Add(turno);
            await contexto.SaveChangesAsync();
        }

        // Un contexto nuevo para que el guardado no tenga trucos de tracking.
        await using var verificar = CrearContexto();
        var leido = await verificar.Turnos.SingleAsync(t => t.Id == turno.Id);

        Assert.Equal(DateTimeKind.Utc, leido.CreadoEn.Kind);
        Assert.Equal(DateTimeKind.Utc, leido.ConfirmadoEn!.Value.Kind);
    }

    private sealed record Semilla(
        Guid LavaderoId,
        Guid PuestoId,
        Guid TipoLavadoId,
        Guid ClienteId);
}