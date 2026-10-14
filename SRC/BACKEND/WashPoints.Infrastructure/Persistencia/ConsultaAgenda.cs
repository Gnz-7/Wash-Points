using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;
using WashPoints.Infrastructure.Tiempo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF de <see cref="IConsultaAgenda"/> (CU-06, SPECS/slice-04).
///
/// Filtra sobre la columna <c>tsrange</c> con <c>lower(rango)</c> en hora local
/// (misma conversión que <see cref="DisponibilidadConsulta"/> y
/// <see cref="OcupacionRepositorio"/>, ARCHITECTURE §10.6). El estado se filtra
/// en memoria para no tener que serializar el enum snake_case al SQL. Los
/// nombres de puesto y tipo de lavado se resuelven con un join en memoria por
/// los pocos cientos de filas de un día de agenda.
/// </summary>
public sealed class ConsultaAgenda : IConsultaAgenda
{
    private const string SqlBase = """
        SELECT id, lavadero_id, puesto_id, cliente_id, tipo_lavado_id,
               rango, estado, origen, precio_sena, mp_preference_id,
               expira_en, idempotency_key, creado_en, confirmado_en, cerrado_en
        FROM turnos
        WHERE lavadero_id = {0}
          AND lower(rango) >= @desdeLocal
          AND lower(rango) < @hastaLocal
          AND (@puestoFiltro IS NULL OR puesto_id = @puestoFiltro)
        ORDER BY lower(rango)
        """;

    private readonly WashPointsDbContext _contexto;

    public ConsultaAgenda(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<IReadOnlyList<TurnoAgenda>> ListarAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        Guid? puestoId,
        EstadoTurno? estado,
        CancellationToken ct)
    {
        if (hastaUtc <= desdeUtc)
        {
            throw new ArgumentException(
                "El fin de la ventana debe ser posterior a su inicio.", nameof(hastaUtc));
        }

        var desdeLocal = new NpgsqlParameter("desdeLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(desdeUtc),
        };
        var hastaLocal = new NpgsqlParameter("hastaLocal", NpgsqlDbType.Timestamp)
        {
            Value = ConversorHorario.ALocal(hastaUtc),
        };
        var puestoFiltro = new NpgsqlParameter("puestoFiltro", NpgsqlDbType.Uuid)
        {
            Value = (object?)puestoId ?? DBNull.Value,
        };

        var turnos = await _contexto.Turnos
            .FromSqlRaw(SqlBase, lavaderoId, desdeLocal, hastaLocal, puestoFiltro)
            .AsNoTracking()
            .ToArrayAsync(ct);

        if (estado is not null)
        {
            turnos = turnos.Where(t => t.Estado == estado).ToArray();
        }

        if (turnos.Length == 0)
        {
            return [];
        }

        var puestos = await _contexto.Puestos
            .AsNoTracking()
            .Where(p => p.LavaderoId == lavaderoId)
            .ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        var tipos = await _contexto.TiposLavado
            .AsNoTracking()
            .Where(t => t.LavaderoId == lavaderoId)
            .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        return turnos
            .Select(t => new TurnoAgenda(
                t.Id,
                t.PuestoId,
                puestos.TryGetValue(t.PuestoId, out var nombrePuesto) ? nombrePuesto : string.Empty,
                t.TipoLavadoId,
                tipos.TryGetValue(t.TipoLavadoId, out var nombreTipo) ? nombreTipo : string.Empty,
                t.Origen,
                t.Estado,
                t.InicioUtc,
                t.FinUtc))
            .ToArray();
    }
}
