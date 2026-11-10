using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-185: backfill del slice-17 (SPECS/slice-17, D-17.2, R-1).
///
/// La migración <c>Slice17HorariosReglasPuestos</c> siembra la semana por
/// defecto (lunes a sábado, 08:00–20:00) en todo lavadero que ya existía. Sin
/// ese backfill, los lavaderos previos al slice quedan cerrados —«sin filas es
/// cerrado»— y toda reserva suya responde 422.
///
/// El test se hace en una base aparte porque la del fixture ya tiene todas las
/// migraciones aplicadas: no hay forma de observar el efecto del backfill sobre
/// una fila preexistente. Se migra hasta la última migración anterior al
/// slice-17, se inserta un lavadero «viejo» y recién entonces se aplica el
/// resto. Si alguien borra el <c>migrationBuilder.Sql(...)</c> del backfill,
/// este test falla.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class HorariosBackfillTest : IClassFixture<ContenedorPostgres>, IAsyncLifetime
{
    /// <summary>Última migración previa al slice-17.</summary>
    private const string MigracionPreviaAlSlice17 = "20261107143732_Slice15ReembolsosEjecucion";

    private const string NombreBase = "backfill_slice17";

    private readonly ContenedorPostgres _baseDeDatos;

    public HorariosBackfillTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    private string CadenaConexionBase =>
        new NpgsqlConnectionStringBuilder(_baseDeDatos.CadenaConexion)
        {
            Database = NombreBase,
        }.ConnectionString;

    private WashPointsDbContext CrearContexto() =>
        new(new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(CadenaConexionBase)
            .Options);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await _baseDeDatos.EjecutarSqlAsync($"DROP DATABASE IF EXISTS {NombreBase} WITH (FORCE);");
    }

    /// <summary>Deja la base del test en la migración previa al slice-17.</summary>
    private async Task MigrarHastaAntesDelSlice17Async()
    {
        NpgsqlConnection.ClearAllPools();
        await _baseDeDatos.EjecutarSqlAsync($"DROP DATABASE IF EXISTS {NombreBase} WITH (FORCE);");
        await _baseDeDatos.EjecutarSqlAsync($"CREATE DATABASE {NombreBase};");

        await using var contexto = CrearContexto();
        await contexto.GetService<IMigrator>().MigrateAsync(MigracionPreviaAlSlice17);
    }

    [Fact]
    public async Task ElLavaderoPrevioAlSliceQuedaConLaSemanaLunASabado()
    {
        await MigrarHastaAntesDelSlice17Async();

        Guid lavaderoId;
        await using (var contexto = CrearContexto())
        {
            var previo = Lavadero.Crear("Lavadero Previo");
            contexto.Lavaderos.Add(previo);
            await contexto.SaveChangesAsync();
            lavaderoId = previo.Id;
        }

        await using (var contexto = CrearContexto())
        {
            await contexto.GetService<IMigrator>().MigrateAsync();
        }

        await using (var contexto = CrearContexto())
        {
            var franjas = await contexto.HorariosAtencion
                .Where(f => f.LavaderoId == lavaderoId)
                .OrderBy(f => f.DiaSemana)
                .ToArrayAsync();

            Assert.Equal(6, franjas.Length);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, franjas.Select(f => f.DiaSemana).ToArray());
            Assert.All(franjas, franja =>
            {
                Assert.Equal(new TimeOnly(8, 0), franja.Apertura);
                Assert.Equal(new TimeOnly(20, 0), franja.Cierre);
                Assert.True(franja.Activo);
            });
        }
    }
}
