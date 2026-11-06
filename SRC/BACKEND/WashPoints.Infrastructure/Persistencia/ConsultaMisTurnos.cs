using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.MisTurnos;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Lectura del historial del cliente para «Mis turnos» (SPECS/slice-14 T-144).
///
/// <para>
/// Es una consulta de solo lectura: no arbitra ocupación ni cambia estados, por
/// eso no pasa por <c>OcupacionService</c>. Une el turno con su lavadero y su
/// tipo de lavado para mostrarlo en el panel sin que el cliente tenga que pedir
/// tres endpoints; el comprobante y el reembolso se resuelven con dos consultas
/// por lote para no caer en N+1.
/// </para>
/// <para>
/// El orden se aplica en memoria porque <c>rango</c> es un <c>tsrange</c>
/// convertido y EF no puede proyectar <c>InicioUtc</c> en SQL: se traen las
/// filas del cliente (un puñado) y se ordenan del más reciente al más viejo.
/// </para>
/// </summary>
public sealed class ConsultaMisTurnos : IConsultaMisTurnos
{
    private readonly WashPointsDbContext _contexto;

    public ConsultaMisTurnos(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<IReadOnlyList<TurnoCliente>> ListarAsync(
        Guid clienteId,
        CancellationToken ct)
    {
        var filas = await (
                from turno in _contexto.Turnos.AsNoTracking()
                join lavadero in _contexto.Lavaderos on turno.LavaderoId equals lavadero.Id
                join tipo in _contexto.TiposLavado on turno.TipoLavadoId equals tipo.Id
                where turno.ClienteId == clienteId
                select new
                {
                    Turno = turno,
                    LavaderoNombre = lavadero.Nombre,
                    lavadero.Direccion,
                    TipoNombre = tipo.Nombre,
                    tipo.DuracionMinutos,
                })
            .ToArrayAsync(ct);

        if (filas.Length == 0)
        {
            return [];
        }

        var turnoIds = filas.Select(fila => fila.Turno.Id).ToArray();

        var conComprobante = await _contexto.Comprobantes
            .AsNoTracking()
            .Where(comprobante => turnoIds.Contains(comprobante.TurnoId))
            .Select(comprobante => comprobante.TurnoId)
            .ToArrayAsync(ct);

        var reembolsos = await _contexto.Reembolsos
            .AsNoTracking()
            .Where(reembolso => turnoIds.Contains(reembolso.TurnoId))
            .ToArrayAsync(ct);

        return filas
            .OrderByDescending(fila => fila.Turno.InicioUtc)
            .Select(fila =>
            {
                var reembolso = reembolsos
                    .FirstOrDefault(candidato => candidato.TurnoId == fila.Turno.Id);

                return new TurnoCliente(
                    fila.Turno.Id,
                    fila.Turno.LavaderoId,
                    fila.LavaderoNombre,
                    fila.Direccion,
                    fila.TipoNombre,
                    fila.DuracionMinutos,
                    fila.Turno.InicioUtc,
                    fila.Turno.FinUtc,
                    fila.Turno.Estado,
                    fila.Turno.PrecioTotal,
                    fila.Turno.PrecioSena,
                    conComprobante.Contains(fila.Turno.Id),
                    reembolso?.Estado,
                    reembolso?.Motivo,
                    reembolso?.Monto);
            })
            .ToArray();
    }
}
