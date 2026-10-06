using Xunit;
using WashPoints.IntegrationTests.Infra;

namespace WashPoints.IntegrationTests;

/// <summary>
/// Test de humo: prueba que el motor de pruebas de integración funciona.
///
/// Si este test falla, ninguna otra prueba de integración es creíble. Corre
/// primero en el orden del plan (T-02) precisamente para que un fallo de
/// entorno no se confunda con un fallo de código.
///
/// Ver SPECS/slice-00-nucleo-turnos.md T-02.
/// </summary>
[Trait("Categoría", "Integración")]
public sealed class HumoTest : IClassFixture<ContenedorPostgres>
{
    private readonly ContenedorPostgres _baseDeDatos;

    public HumoTest(ContenedorPostgres baseDeDatos) => _baseDeDatos = baseDeDatos;

    [Fact]
    public async Task PostgresResponde()
    {
        var version = await _baseDeDatos.ConsultarEscalarAsync<string>("SELECT version()");

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains("PostgreSQL", version, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtensionBtreeGistEstaDisponible()
    {
        // btree_gist es requisito de la restricción EXCLUDE USING gist (T-04).
        // Sin ella, la exclusión de turnos no se puede declarar.
        await _baseDeDatos.EjecutarSqlAsync("CREATE EXTENSION IF NOT EXISTS btree_gist");

        var instalada = await _baseDeDatos.ConsultarEscalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist')");

        Assert.True(instalada);
    }

    [Fact]
    public async Task RangoSemiabiertoDosAdyacentesNoSeSolapan()
    {
        // Verifica la semántica [inicio, fin) de ARCHITECTURE §4.1, sobre la
        // que descansa toda la exclusión de turnos. Se prueba en Postgres real
        // porque esa semántica es del motor, no de la aplicación.
        //
        // Cada consulta es independiente (una conexión por llamada), por eso
        // no se usa una tabla temporal: no sobreviviría a la conexión.
        //
        //   10:00-11:00 y 11:00-12:00  -> NO se solapan (adyacentes)
        //   10:00-11:00 y 10:30-11:30  -> sí se solapan (superpuestos)
        var solapanLasAdyacentes = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT tsrange('2026-10-10 10:00', '2026-10-10 11:00')
                && tsrange('2026-10-10 11:00', '2026-10-10 12:00')
            """);

        var solapanLasSuperpuestas = await _baseDeDatos.ConsultarEscalarAsync<bool>("""
            SELECT tsrange('2026-10-10 10:00', '2026-10-10 11:00')
                && tsrange('2026-10-10 10:30', '2026-10-10 11:30')
            """);

        Assert.False(solapanLasAdyacentes, "Dos turnos contiguos no deben chocar.");
        Assert.True(solapanLasSuperpuestas, "Dos turnos superpuestos sí deben chocar.");
    }
}
