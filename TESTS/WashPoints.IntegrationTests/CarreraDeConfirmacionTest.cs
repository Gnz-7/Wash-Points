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
/// T-06: el criterio de aceptación del slice.
///
/// Seis confirmaciones concurrentes sobre el mismo puesto y rango (seis
/// contextos EF, uno por "request HTTP"). La EXCLUDE <c>turnos_no_solapamiento</c>
/// debe admitir exactamente un ganador; los demás requests reciben
/// <see cref="TurnoConflictoException"/> (409 en la capa HTTP), sin error 500.
///
/// La base es la que arbitra: no hay locks de aplicación ni "SELECT ... FOR
/// UPDATE". Ver SPECS/slice-00-nucleo-turnos.md T-06 y ARCHITECTURE §4.2.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class CarreraDeConfirmacionTest : IClassFixture<ContenedorPostgres>, IDisposable
{
    private const int Concurrentes = 6;

    private readonly ContenedorPostgres _baseDeDatos;

    public CarreraDeConfirmacionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    private async Task<(Guid PuestoId, Guid TipoLavadoId, Guid ClienteId, Guid LavaderoId)> SembrarCatalogo()
    {
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        return (puesto.Id, tipo.Id, cliente.Id, lavadero.Id);
    }

    [Fact]
    public async Task SeisConfirmacionesConcurrentesDejanUnSoloOcupante()
    {
        var (puestoId, tipoLavadoId, clienteId, lavaderoId) = await SembrarCatalogo();
        var inicioUtc = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var finUtc = inicioUtc.AddHours(1);

        // Cada request reserva (pendiente_pago, no ocupa) en su propio contexto
        // y devuelve el id del turno para la corrida de confirmaciones.
        var ids = new List<Guid>(Concurrentes);
        for (var i = 0; i < Concurrentes; i++)
        {
            await using var contextoReserva = CrearContexto();
            var servicioReserva = new OcupacionServicio(new OcupacionRepositorio(contextoReserva));

            var turno = Turno.Crear(
                lavaderoId: lavaderoId,
                puestoId: puestoId,
                clienteId: clienteId,
                tipoLavadoId: tipoLavadoId,
                inicioUtc: inicioUtc,
                finUtc: finUtc,
                precioSena: 3000m);

            ids.Add(await servicioReserva.ReservarAsync(turno, CancellationToken.None));
        }

        // Se lanzan todas las confirmaciones a la vez (Task.WhenAll). Cada una
        // corre en su propio contexto (un "request HTTP").
        async Task<(Guid Id, TurnoConflictoException? Conflicto)> Confirmar(int i)
        {
            await using var contexto = CrearContexto();
            var servicio = new OcupacionServicio(new OcupacionRepositorio(contexto));

            try
            {
                await servicio.ConfirmarOcupacionAsync(ids[i], CancellationToken.None);
                return (ids[i], null);
            }
            catch (TurnoConflictoException ex)
            {
                return (ids[i], ex);
            }
        }

        var resultados = await Task.WhenAll(
            Enumerable.Range(0, Concurrentes).Select(Confirmar));

        // La EXCLUDE admite exactamente un ganador; el resto 409.
        var ganadores = resultados.Where(r => r.Conflicto is null).ToArray();
        var perdedores = resultados.Where(r => r.Conflicto is not null).ToArray();

        Assert.Single(ganadores);
        Assert.Equal(Concurrentes - 1, perdedores.Length);
        Assert.All(perdedores, r =>
            Assert.Contains("el puesto tiene otro turno", r.Conflicto!.Message, StringComparison.OrdinalIgnoreCase));

        // RB-2: la fuente de verdad es la base. Solo un turno quedó confirmado
        // sobre el puesto para ese rango; los perdedores siguen pendientes.
        // El filtro de rango se hace en memoria: InicioUtc/FinUtc son propiedades
        // calculadas de la columna `rango` (tsrange), no columnas SQL.
        await using var verificar = CrearContexto();
        var turnosDelPuesto = await verificar.Turnos
            .Where(t => t.PuestoId == puestoId)
            .ToListAsync();

        var confirmados = turnosDelPuesto
            .Count(t => t.Estado == EstadoTurno.Confirmado
                        && t.InicioUtc == inicioUtc
                        && t.FinUtc == finUtc);
        var pendientes = turnosDelPuesto
            .Count(t => t.Estado == EstadoTurno.PendientePago);

        Assert.Equal(1, confirmados);
        Assert.Equal(Concurrentes - 1, pendientes);
    }
}