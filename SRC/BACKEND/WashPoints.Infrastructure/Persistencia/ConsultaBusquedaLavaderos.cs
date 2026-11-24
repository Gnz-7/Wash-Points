using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WashPoints.Domain.Catalogo;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Búsqueda por proximidad resuelta en la base local (D-08.2): Haversine en
/// SQL directo, sin PostGIS ni proveedor externo. La ocupación viaja en la
/// misma consulta para no multiplicar viajes por lavadero (D-08.9).
///
/// La búsqueda por texto (D-21.17) reutiliza la misma CTE y proyección, pero
/// filtra por nombre/dirección y no aplica el radio: un lavadero puede estar
/// lejos del cliente y aun así ser el que buscó por nombre.
/// </summary>
public sealed class ConsultaBusquedaLavaderos : IBusquedaLavaderos
{
    private const string Candidatos = """
        WITH candidatos AS (
            SELECT l.id,
                   l.nombre,
                   l.direccion,
                   l.latitud,
                   l.longitud,
                   6371000.0 * 2.0 * asin(sqrt(
                       least(1.0, greatest(0.0,
                           power(sin(radians(l.latitud - @latitud) / 2.0), 2)
                           + cos(radians(@latitud)) * cos(radians(l.latitud))
                             * power(sin(radians(l.longitud - @longitud) / 2.0), 2)
                       ))
                   )) AS distancia_metros
              FROM lavaderos l
             WHERE l.activo
               AND l.latitud IS NOT NULL
               AND l.longitud IS NOT NULL
        )
        """;

    private const string Proyeccion = """

        SELECT c.id,
               c.nombre,
               c.direccion,
               c.distancia_metros,
               c.latitud,
               c.longitud,
               (SELECT count(*)::int
                  FROM puestos p
                 WHERE p.lavadero_id = c.id) AS puestos_totales,
               (SELECT count(*)::int
                  FROM puestos p
                 WHERE p.lavadero_id = c.id AND p.activo) AS puestos_activos,
               (SELECT count(DISTINCT t.puesto_id)::int
                  FROM turnos t
                 WHERE t.lavadero_id = c.id
                   AND t.estado IN ('confirmado', 'en_curso')
                   AND t.rango @> @ahoraLocal) AS ocupados_ahora
          FROM candidatos c
        """;

    private const string SqlBase = Candidatos + Proyeccion + """

         WHERE c.distancia_metros <= @radioMetros
         ORDER BY c.distancia_metros
        """;

    private const string SqlTexto = Candidatos + Proyeccion + """

         WHERE (strpos(lower(c.nombre), lower(@texto)) > 0
                OR strpos(lower(coalesce(c.direccion, '')), lower(@texto)) > 0)
         ORDER BY c.distancia_metros
         LIMIT @limite
        """;

    private readonly WashPointsDbContext _contexto;

    public ConsultaBusquedaLavaderos(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<IReadOnlyList<LavaderoCercano>> BuscarCercanosAsync(
        double latitud,
        double longitud,
        int radioMetros,
        DateTime ahoraUtc,
        CancellationToken ct = default)
    {
        var conexion = (NpgsqlConnection)_contexto.Database.GetDbConnection();
        var abrir = conexion.State != ConnectionState.Open;
        if (abrir)
        {
            await conexion.OpenAsync(ct);
        }

        try
        {
            await using var comando = new NpgsqlCommand(SqlBase, conexion);
            AgregarParametrosDeReferencia(comando, latitud, longitud, ahoraUtc);
            comando.Parameters.Add(new NpgsqlParameter("radioMetros", NpgsqlDbType.Double)
            {
                Value = (double)radioMetros,
            });

            return await LeerAsync(comando, ct);
        }
        finally
        {
            if (abrir)
            {
                await conexion.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<LavaderoCercano>> BuscarPorTextoAsync(
        double latitud,
        double longitud,
        string texto,
        int limite,
        DateTime ahoraUtc,
        CancellationToken ct = default)
    {
        var conexion = (NpgsqlConnection)_contexto.Database.GetDbConnection();
        var abrir = conexion.State != ConnectionState.Open;
        if (abrir)
        {
            await conexion.OpenAsync(ct);
        }

        try
        {
            await using var comando = new NpgsqlCommand(SqlTexto, conexion);
            AgregarParametrosDeReferencia(comando, latitud, longitud, ahoraUtc);
            comando.Parameters.Add(new NpgsqlParameter("texto", NpgsqlDbType.Text) { Value = texto });
            comando.Parameters.Add(new NpgsqlParameter("limite", NpgsqlDbType.Integer) { Value = limite });

            return await LeerAsync(comando, ct);
        }
        finally
        {
            if (abrir)
            {
                await conexion.CloseAsync();
            }
        }
    }

    private static void AgregarParametrosDeReferencia(
        NpgsqlCommand comando,
        double latitud,
        double longitud,
        DateTime ahoraUtc)
    {
        comando.Parameters.Add(new NpgsqlParameter("latitud", NpgsqlDbType.Double) { Value = latitud });
        comando.Parameters.Add(new NpgsqlParameter("longitud", NpgsqlDbType.Double) { Value = longitud });
        // turnos.rango guarda hora local sin zona (ARCHITECTURE §10.6).
        comando.Parameters.Add(new NpgsqlParameter("ahoraLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(ahoraUtc),
        });
    }

    private static async Task<IReadOnlyList<LavaderoCercano>> LeerAsync(
        NpgsqlCommand comando,
        CancellationToken ct)
    {
        var resultados = new List<LavaderoCercano>();
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            resultados.Add(new LavaderoCercano(
                lector.GetGuid(0),
                lector.GetString(1),
                lector.IsDBNull(2) ? null : lector.GetString(2),
                lector.GetDouble(3),
                lector.GetDouble(4),
                lector.GetDouble(5),
                lector.GetInt32(6),
                lector.GetInt32(7),
                lector.GetInt32(8)));
        }

        return resultados;
    }
}
