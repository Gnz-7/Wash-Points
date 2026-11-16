using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WashPoints.Infrastructure.Persistencia;
using WashPoints.IntegrationTests.Infra;
using Xunit;

namespace WashPoints.IntegrationTests;

/// <summary>
/// T-216 (SPECS/slice-20, D-20.8): backfill de la suscripción mensual.
///
/// La migración <c>Slice20SuscripcionVinculacion</c> deja con
/// <c>suscripcion_activa = true</c> a todo lavadero que ya existía, para que
/// el despliegue no interrumpa a los operadores en actividad (CA-S20.8). El
/// alta de ahora en adelante nace pendiente (lo cubren los tests de dominio);
/// este test protege sólo el backfill. Se hace en una base aparte por la
/// misma razón que <see cref="HorariosBackfillTest"/>: el fixture ya tiene
/// todas las migraciones aplicadas, así que no hay forma de observar el
/// efecto del backfill sobre una fila preexistente.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class BackfillSuscripcionTest : IClassFixture<ContenedorPostgres>, IAsyncLifetime
{
    /// <summary>Última migración previa al slice-20.</summary>
    private const string MigracionPreviaAlSlice20 = "20261108013502_Slice17HorariosReglasPuestos";

    private const string NombreBase = "backfill_slice20";

    private readonly ContenedorPostgres _baseDeDatos;

    public BackfillSuscripcionTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

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

    /// <summary>Deja la base del test en la migración previa al slice-20.</summary>
    private async Task MigrarHastaAntesDelSlice20Async()
    {
        NpgsqlConnection.ClearAllPools();
        await _baseDeDatos.EjecutarSqlAsync($"DROP DATABASE IF EXISTS {NombreBase} WITH (FORCE);");
        await _baseDeDatos.EjecutarSqlAsync($"CREATE DATABASE {NombreBase};");

        await using var contexto = CrearContexto();
        await contexto.GetService<IMigrator>().MigrateAsync(MigracionPreviaAlSlice20);
    }

    [Fact]
    public async Task ElLavaderoPrevioAlSlice20QuedaConSuscripcionActiva()
    {
        await MigrarHastaAntesDelSlice20Async();

        Guid lavaderoId;
        await using (var contexto = CrearContexto())
        {
            // Insert crudo: el lavadero «previo» tiene que existir con el
            // esquema anterior al slice-20; el modelo actual de EF ya conoce
            // las columnas nuevas y no puede insertar contra el esquema viejo.
            var conexion = contexto.Database.GetDbConnection();
            await conexion.OpenAsync();
            lavaderoId = Guid.NewGuid();
            using var comando = conexion.CreateCommand();
            comando.CommandText =
                $"INSERT INTO lavaderos (id, nombre, activo) VALUES ('{lavaderoId}', 'Lavadero Previo', true)";
            await comando.ExecuteNonQueryAsync();
        }

        await using (var contexto = CrearContexto())
        {
            await contexto.GetService<IMigrator>().MigrateAsync();
        }

        await using (var contexto = CrearContexto())
        {
            var conexion = contexto.Database.GetDbConnection();
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = $"SELECT suscripcion_activa FROM lavaderos WHERE id = '{lavaderoId}'";
            var activa = await comando.ExecuteScalarAsync();

            Assert.True(
                activa as bool? == true,
                "El backfill no marcó activa la suscripción del lavadero previo.");
        }
    }
}
