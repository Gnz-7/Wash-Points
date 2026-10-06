using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Consulta de disponibilidad (IDisponibilidadConsulta) contra la tabla
/// <c>turnos</c>.
///
/// El solapamiento se resuelve en el motor con el operador <c>&&</c> de rango
/// sobre la propia columna <c>tsrange</c>: es la misma fuente que arbitró la
/// EXCLUDE al confirmar (RB-2). El rango de consulta se traduce de UTC a la
/// hora local del lavadero antes de comparar (ARCHITECTURE §10.6), y los
/// turnos se materializan con el convertidor de rango que los devuelve a UTC.
/// </summary>
public sealed class DisponibilidadConsulta : IDisponibilidadConsulta
{
    private const string SqlBase = """
        SELECT id, lavadero_id, puesto_id, cliente_id, tipo_lavado_id,
               rango, estado, origen, precio_sena, mp_preference_id,
               creado_en, confirmado_en, cerrado_en
        FROM turnos
        WHERE lavadero_id = {0}
          AND estado IN ('confirmado', 'en_curso')
          AND rango && tsrange(@desdeLocal, @hastaLocal)
        ORDER BY rango
        """;

    private readonly WashPointsDbContext _contexto;

    public DisponibilidadConsulta(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<IReadOnlyList<IntervaloOcupado>> ObtenerOcupadosAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct)
    {
        if (hastaUtc <= desdeUtc)
        {
            throw new ArgumentException(
                "El fin de la ventana debe ser posterior a su inicio.", nameof(hastaUtc));
        }

        // Los extremos viajan como `timestamp` SIN zona (la columna es tsrange):
        // así Npgsql no intenta interpretarlos como timestamptz y no se mezclan
        // Kinds. El tsrange de la EXCLUDE también guardó hora local.
        var desdeLocal = new NpgsqlParameter("desdeLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(desdeUtc),
        };
        var hastaLocal = new NpgsqlParameter("hastaLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(hastaUtc),
        };

        var turnos = await _contexto.Turnos
            .FromSqlRaw(SqlBase, lavaderoId, desdeLocal, hastaLocal)
            .ToArrayAsync(ct);

        return turnos
            .Select(t => new IntervaloOcupado(
                t.Id, t.PuestoId, t.Origen, t.InicioUtc, t.FinUtc))
            .ToArray();
    }
}