using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Fábrica de diseño para <c>dotnet ef</c>: permite crear migraciones fuera del
/// runtime de la API, cuando no hay un host que inyecte opciones.
///
/// Lee la cadena de conexión de la configuración de mando si existe; si no,
/// usará una cadena local por defecto (la misma que el compose de desarrollo).
/// </summary>
public sealed class WashPointsDbContextFactory : IDesignTimeDbContextFactory<WashPointsDbContext>
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=washpoints;Username=washpoints;Password=washpoints";

    public WashPointsDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cadena = config.GetConnectionString("WashPoints") ?? DefaultConnection;

        var opciones = new DbContextOptionsBuilder<WashPointsDbContext>()
            .UseNpgsql(
                cadena,
                npgsql => npgsql.MigrationsAssembly("WashPoints.Infrastructure"))
            .Options;

        return new WashPointsDbContext(opciones);
    }
}