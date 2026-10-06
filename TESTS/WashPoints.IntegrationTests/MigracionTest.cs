using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Aplica la migración inicial contra Postgres real y verifica lo que EF no
/// puede validar por sí solo: la restricción EXCLUDE y la extensión de la que
/// depende.
///
/// Ver SPECS/slice-00-nucleo-turnos.md T-04.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class MigracionTest : IClassFixture<ContenedorPostgres>, IAsyncLifetime
{
    private readonly ContenedorPostgres _baseDeDatos;

    public MigracionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    private WashPointsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(_baseDeDatos.CadenaConexion)
            .Options;
        return new WashPointsDbContext(opciones);
    }

    public async Task InitializeAsync()
    {
        await using var contexto = CrearContexto();
        await contexto.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task LaMigracionAplicaUnaSolaVez()
    {
        await using var contexto = CrearContexto();
        var aplicadas = await contexto.Database.GetAppliedMigrationsAsync();

        Assert.Contains(aplicadas, m => m.EndsWith("_Inicial", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LaRestriccionDeNoSolapamientoExiste()
    {
        var existe = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'turnos_no_solapamiento'
                  AND contype = 'x'
            )
            """);

        Assert.True(existe, "La restricción de exclusión no se creó en la migración.");
    }

    [Fact]
    public async Task BtreeGistQuedaInstaladaPorLaMigracion()
    {
        // La migración ejecuta CREATE EXTENSION btree_gist; sin ella, el
        // EXCLUDE con igualdad sobre uuid no puede crearse.
        var instalada = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')");

        Assert.True(instalada);
    }

    [Fact]
    public async Task GuardarYLeerTurnoResuelveElRangoEnUtc()
    {
        // Recorrido de ida y vuelta del convertidor: el dominio guarda UTC, la
        // base guarda hora local, y al leer ambos turnos vuelven a ser el mismo.
        // Es la prueba cruzada de T-09 en su forma mínima.
        await using var contexto = CrearContexto();

        var lavadero = Lavadero.Crear("Lavadero Central");
        var puesto = Puesto.Crear(lavadero.Id, "Puesto 1");
        var tipo = TipoLavado.Crear(lavadero.Id, "Exterior", 30, 15000m, 3000m);
        var cliente = Cliente.Crear("Cliente de Prueba");

        contexto.AddRange(lavadero, puesto, tipo, cliente);
        await contexto.SaveChangesAsync();

        var inicioUtc = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);
        var finUtc = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var turno = Turno.Crear(
            lavaderoId: lavadero.Id,
            puestoId: puesto.Id,
            clienteId: cliente.Id,
            tipoLavadoId: tipo.Id,
            inicioUtc: inicioUtc,
            finUtc: finUtc,
            precioSena: tipo.PrecioSena);

        contexto.Turnos.Add(turno);
        await contexto.SaveChangesAsync();

        // El guardado aplica el convertidor; el siguiente contexto re-lee lo
        // persistido, de modo que no quedan trucos de cache de rastreo.
        await using var contexto2 = CrearContexto();
        var turnoLeido = await contexto2.Turnos
            .Where(t => t.Id == turno.Id)
            .SingleAsync();

        Assert.Equal(inicioUtc, turnoLeido.InicioUtc);
        Assert.Equal(finUtc, turnoLeido.FinUtc);
    }
}
