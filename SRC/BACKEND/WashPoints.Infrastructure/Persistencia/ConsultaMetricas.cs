using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WashPoints.Domain.Metricas;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Read-model de solo lectura para el panel de métricas (CU-09).
/// Consulta el esquema existente (<c>turnos</c>, <c>pagos</c>, <c>puestos</c> y
/// <c>tipos_lavado</c>) sin escribir ni encolar en el outbox (CA-S6.7).
/// <c>turnos.rango</c> guarda hora local sin zona: los límites de la ventana se
/// convierten con <see cref="ConversorHorario"/>; <c>pagos.procesado_en</c> es
/// <c>timestamptz</c> y se compara con instantes UTC.
/// </summary>
public sealed class ConsultaMetricas : IMetricasConsulta
{
    private const string SqlResumen = """
        SELECT
            (SELECT count(*)::int FROM puestos p WHERE p.lavadero_id = @lavaderoId) AS puestos_total,
            (SELECT count(*)::int FROM puestos p WHERE p.lavadero_id = @lavaderoId AND p.activo) AS puestos_activos,
            (SELECT count(*)::int
               FROM turnos t
              WHERE t.lavadero_id = @lavaderoId
                AND t.estado IN ('confirmado', 'en_curso')
                AND t.rango @> @ahoraLocal) AS puestos_ocupados,
            now() AS calculado_en,
            COALESCE(
                (SELECT sum(t.precio_sena)
                   FROM turnos t
                   JOIN pagos pg ON pg.turno_id = t.id
                  WHERE t.lavadero_id = @lavaderoId
                    AND pg.resultado = 'approved'
                    AND pg.procesado_en >= @desdeUtc
                    AND pg.procesado_en < @hastaUtc), 0)::numeric AS senas_cobradas,
            COALESCE(
                (SELECT sum(tl.precio_total)
                   FROM turnos t
                   JOIN tipos_lavado tl ON tl.id = t.tipo_lavado_id
                  WHERE t.lavadero_id = @lavaderoId
                    AND t.estado IN ('finalizado', 'en_curso')
                    AND lower(t.rango) >= @desdeLocal
                    AND lower(t.rango) < @hastaLocal), 0)::numeric AS facturado_total,
            COALESCE(
                (SELECT sum(tl.precio_total)
                   FROM turnos t
                   JOIN tipos_lavado tl ON tl.id = t.tipo_lavado_id
                  WHERE t.lavadero_id = @lavaderoId
                    AND t.estado IN ('finalizado', 'en_curso')
                    AND t.origen = 'espontanea'
                    AND lower(t.rango) >= @desdeLocal
                    AND lower(t.rango) < @hastaLocal), 0)::numeric AS facturado_mostrador,
            (SELECT count(*)::int
               FROM turnos t
              WHERE t.lavadero_id = @lavaderoId
                AND t.estado IN ('finalizado', 'en_curso')
                AND lower(t.rango) >= @desdeLocal
                AND lower(t.rango) < @hastaLocal) AS atendidos,
            (SELECT count(*)::int
               FROM turnos t
              WHERE t.lavadero_id = @lavaderoId
                AND t.estado IN ('finalizado', 'en_curso')
                AND t.origen = 'espontanea'
                AND lower(t.rango) >= @desdeLocal
                AND lower(t.rango) < @hastaLocal) AS espontaneos,
            (SELECT count(*)::int
               FROM turnos t
              WHERE t.lavadero_id = @lavaderoId
                AND t.estado = 'no_presentado'
                AND lower(t.rango) >= @desdeLocal
                AND lower(t.rango) < @hastaLocal) AS no_presentados,
            (SELECT count(*)::int
               FROM turnos t
              WHERE t.lavadero_id = @lavaderoId
                AND t.estado = 'cancelado'
                AND lower(t.rango) >= @desdeLocal
                AND lower(t.rango) < @hastaLocal) AS cancelaciones
        """;

    private const string SqlOcupacion = """
        SELECT p.id,
               p.nombre,
               COALESCE(
                   sum(extract(epoch FROM (
                       least(upper(t.rango), @hastaLocal)
                       - greatest(lower(t.rango), @desdeLocal)
                   ))) FILTER (WHERE t.rango IS NOT NULL), 0)::numeric AS segundos_ocupados
          FROM puestos p
          LEFT JOIN turnos t
                 ON t.puesto_id = p.id
                AND t.lavadero_id = p.lavadero_id
                AND t.estado IN ('confirmado', 'en_curso', 'finalizado')
                AND t.rango && tsrange(@desdeLocal, @hastaLocal)
         WHERE p.lavadero_id = @lavaderoId
           AND (@puestoId IS NULL OR p.id = @puestoId)
         GROUP BY p.id, p.nombre
         ORDER BY p.nombre
        """;

    private readonly WashPointsDbContext _contexto;

    public ConsultaMetricas(WashPointsDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<ResumenMetricas> ObtenerResumenAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct = default)
    {
        var (conexion, cerrar) = await AbrirAsync(ct);
        try
        {
            await using var comando = new NpgsqlCommand(SqlResumen, conexion);
            comando.Parameters.Add(new NpgsqlParameter("lavaderoId", NpgsqlDbType.Uuid) { Value = lavaderoId });
            comando.Parameters.Add(new NpgsqlParameter("desdeLocal", NpgsqlDbType.Timestamp) { Value = ConversorHorario.ALocal(desdeUtc) });
            comando.Parameters.Add(new NpgsqlParameter("hastaLocal", NpgsqlDbType.Timestamp) { Value = ConversorHorario.ALocal(hastaUtc) });
            comando.Parameters.Add(new NpgsqlParameter("desdeUtc", NpgsqlDbType.TimestampTz) { Value = InstanteUtc(desdeUtc) });
            comando.Parameters.Add(new NpgsqlParameter("hastaUtc", NpgsqlDbType.TimestampTz) { Value = InstanteUtc(hastaUtc) });
            comando.Parameters.Add(new NpgsqlParameter("ahoraLocal", NpgsqlDbType.Timestamp) { Value = ConversorHorario.ALocal(DateTime.UtcNow) });

            await using var lector = await comando.ExecuteReaderAsync(ct);
            if (!await lector.ReadAsync(ct))
            {
                return new ResumenMetricas(0, 0, 0, DateTime.UtcNow, 0m, 0m, 0m, 0, 0, 0, 0);
            }

            return new ResumenMetricas(
                lector.GetInt32(0),
                lector.GetInt32(1),
                lector.GetInt32(2),
                lector.GetDateTime(3),
                lector.GetDecimal(4),
                lector.GetDecimal(5),
                lector.GetDecimal(6),
                lector.GetInt32(7),
                lector.GetInt32(8),
                lector.GetInt32(9),
                lector.GetInt32(10));
        }
        finally
        {
            if (cerrar)
            {
                await conexion.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<OcupacionPuesto>> ObtenerOcupacionAsync(
        Guid lavaderoId,
        Guid? puestoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct = default)
    {
        var (conexion, cerrar) = await AbrirAsync(ct);
        try
        {
            await using var comando = new NpgsqlCommand(SqlOcupacion, conexion);
            comando.Parameters.Add(new NpgsqlParameter("lavaderoId", NpgsqlDbType.Uuid) { Value = lavaderoId });
            comando.Parameters.Add(new NpgsqlParameter("desdeLocal", NpgsqlDbType.Timestamp) { Value = ConversorHorario.ALocal(desdeUtc) });
            comando.Parameters.Add(new NpgsqlParameter("hastaLocal", NpgsqlDbType.Timestamp) { Value = ConversorHorario.ALocal(hastaUtc) });
            comando.Parameters.Add(new NpgsqlParameter("puestoId", NpgsqlDbType.Uuid) { Value = (object?)puestoId ?? DBNull.Value });

            var puestos = new List<OcupacionPuesto>();
            await using var lector = await comando.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
            {
                var segundos = lector.GetDecimal(2);
                var minutos = (int)Math.Round(segundos / 60m, MidpointRounding.AwayFromZero);
                puestos.Add(new OcupacionPuesto(lector.GetGuid(0), lector.GetString(1), minutos));
            }

            return puestos;
        }
        finally
        {
            if (cerrar)
            {
                await conexion.CloseAsync();
            }
        }
    }

    private async Task<(NpgsqlConnection Conexion, bool Cerrar)> AbrirAsync(CancellationToken ct)
    {
        var conexion = (NpgsqlConnection)_contexto.Database.GetDbConnection();
        if (conexion.State == ConnectionState.Open)
        {
            return (conexion, false);
        }

        await conexion.OpenAsync(ct);
        return (conexion, true);
    }

    private static DateTime InstanteUtc(DateTime valor) =>
        DateTime.SpecifyKind(valor, DateTimeKind.Utc);
}
