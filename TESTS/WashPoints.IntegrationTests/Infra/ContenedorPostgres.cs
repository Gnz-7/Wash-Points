using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using WashPoints.Infrastructure.Persistencia;
using Xunit;

namespace WashPoints.IntegrationTests.Infra;

/// <summary>
/// Contenedor PostgreSQL compartido por toda una clase de test.
///
/// Un contenedor por clase, nunca por test: levantar Postgres por test haría la
/// suite inutilizable en duración. Los tests se aíslan limpiando las tablas
/// entre pruebas, no recreando el motor.
///
/// Ver SPECS/slice-00-nucleo-turnos.md T-02.
/// </summary>
public sealed class ContenedorPostgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string CadenaConexion => _contenedor.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            await _contenedor.StartAsync();

            // Cada clase comparte un contenedor (T-02), pero necesita su
            // propia base migrada. Aplicar acá garantiza que ninguna prueba
            // dependa de otra clase para migrar primero.
            var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
                .UseNpgsql(CadenaConexion)
                .Options;
            await using var contexto = new WashPointsDbContext(opciones);
            await contexto.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            // El plan exige que este fallo se reporte con claridad, no que se
            // saltee en silencio: sin daemon de Docker no hay Testcontainers y
            // T-06 (criterio de aceptación del slice) no se puede ejecutar.
            throw new InvalidOperationException(
                "No se pudo levantar el contenedor de PostgreSQL. " +
                "Verificá que Docker Desktop esté corriendo y volvé a ejecutar la suite. " +
                $"Detalle: {ex.Message}",
                ex);
        }
    }

    public async Task DisposeAsync() => await _contenedor.DisposeAsync();

    /// <summary>SQL crudo contra el contenedor, para sentencias que EF Core no expone.</summary>
    public async Task EjecutarSqlAsync(string sql)
    {
        await using var conexion = new NpgsqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Única fila escalar. Para el test de humo y verificaciones puntuales.</summary>
    public async Task<T?> ConsultarEscalarAsync<T>(string sql)
    {
        await using var conexion = new NpgsqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        var resultado = await comando.ExecuteScalarAsync();
        if (resultado is null || resultado is DBNull)
        {
            return default;
        }
        return (T)Convert.ChangeType(resultado, typeof(T));
    }
}
