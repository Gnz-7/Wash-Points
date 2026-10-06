using Microsoft.EntityFrameworkCore;
using WashPoints.Application.Ocupacion;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-07: la consulta de disponibilidad contra Postgres real.
///
/// CA-S0.5: el endpoint/consulta refleja los turnos que ocupan puesto
/// (<c>confirmado</c> y <c>en_curso</c>) y no muestra los <c>pendiente_pago</c>.
/// El <c>origen</c> distingue reservas app de demanda espontánea (RB-2).
///
/// Ver SPECS/slice-00-nucleo-turnos.md T-07.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class DisponibilidadConsultaTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private readonly ContenedorPostgres _baseDeDatos;

    public DisponibilidadConsultaTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    public void Dispose() => LimpiarTablas().GetAwaiter().GetResult();

    private WashPointsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options;
        return new WashPointsDbContext(opciones);
    }

    private async Task LimpiarTablas()
    {
        await _baseDeDatos.EjecutarSqlAsync("""
            TRUNCATE TABLE turnos, clientes, puestos, tipos_lavado, lavaderos CASCADE;
            """);
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

    private static Turno CrearTurno(Semilla s, DateTime inicioUtc, DateTime finUtc, OrigenTurno origen = OrigenTurno.App) =>
            Turno.Crear(s.LavaderoId, s.PuestoId, s.ClienteId, s.TipoLavadoId,
                inicioUtc, finUtc, 3000m, origen);

    [Fact]
    public async Task LaVentanaDeberiaMostrarSoloLosQueOcupanPuesto()
    {
        var s = await SembrarCatalogo();
        var inicio = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);

        // Uno confirmado, uno en_curso, uno pendiente_pago, en la misma ventana.
        var confirmado = CrearTurno(s, inicio, inicio.AddHours(1));
        confirmado.TransicionarA(EstadoTurno.Confirmado);

        var enCurso = CrearTurno(s, inicio.AddHours(1), inicio.AddHours(2));
        enCurso.TransicionarA(EstadoTurno.Confirmado);
        enCurso.TransicionarA(EstadoTurno.EnCurso);

        var pendiente = CrearTurno(s, inicio.AddHours(2), inicio.AddHours(3));

        await using (var contexto = CrearContexto())
        {
            contexto.Turnos.AddRange(confirmado, enCurso, pendiente);
            await contexto.SaveChangesAsync();
        }

        var consulta = new DisponibilidadConsulta(CrearContexto());
        var ocupados = await consulta.ObtenerOcupadosAsync(
            s.LavaderoId,
            inicio,
            inicio.AddHours(3),
            CancellationToken.None);

        Assert.Equal(2, ocupados.Count);
        Assert.Contains(ocupados, i => i.TurnoId == confirmado.Id);
        Assert.Contains(ocupados, i => i.TurnoId == enCurso.Id);
        Assert.DoesNotContain(ocupados, i => i.TurnoId == pendiente.Id);
    }

    [Fact]
    public async Task LaVentanaDeberiaRespetarElOrigen()
    {
        // RB-2: reservas (app) y demanda espontánea se ven juntas pero
        // distinguibles por origen.
        var s = await SembrarCatalogo();
        var inicio = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);

        var reserva = CrearTurno(s, inicio, inicio.AddHours(1));
        reserva.TransicionarA(EstadoTurno.Confirmado);

        var espontaneo = CrearTurno(s, inicio.AddHours(1), inicio.AddHours(2), OrigenTurno.Espontanea);
        espontaneo.TransicionarA(EstadoTurno.Confirmado);

        await using (var contexto = CrearContexto())
        {
            contexto.Turnos.AddRange(reserva, espontaneo);
            await contexto.SaveChangesAsync();
        }

        var consulta = new DisponibilidadConsulta(CrearContexto());
        var ocupados = await consulta.ObtenerOcupadosAsync(
            s.LavaderoId, inicio, inicio.AddHours(2), CancellationToken.None);

        Assert.Equal(OrigenTurno.App, ocupados.Single(i => i.TurnoId == reserva.Id).Origen);
        Assert.Equal(OrigenTurno.Espontanea, ocupados.Single(i => i.TurnoId == espontaneo.Id).Origen);
    }

    private sealed record Semilla(
        Guid LavaderoId,
        Guid PuestoId,
        Guid TipoLavadoId,
        Guid ClienteId);
}
