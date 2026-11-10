using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Catalogo;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Horarios de atención del lavadero (SPECS/slice-17, T-176, R-1).
///
/// Las franjas se leen siempre activas y ordenadas por día y apertura, que es
/// el orden en el que el panel las pinta y en el que el servicio de slots las
/// recorre. El reemplazo es lógico: las vigentes se desactivan y las nuevas se
/// insertan, así el histórico de ediciones no se pierde.
/// </summary>
public sealed class HorarioRepositorio : IHorarioRepositorio
{
    private readonly WashPointsDbContext _contexto;

    public HorarioRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FranjaAtencion>> ListarAsync(
        Guid lavaderoId, CancellationToken ct) =>
        await _contexto.HorariosAtencion
            .AsNoTracking()
            .Where(f => f.LavaderoId == lavaderoId && f.Activo)
            .OrderBy(f => f.DiaSemana)
            .ThenBy(f => f.Apertura)
            .ToArrayAsync(ct);

    /// <inheritdoc />
    public async Task AgregarRangoAsync(
        IEnumerable<FranjaAtencion> franjas, CancellationToken ct) =>
        await _contexto.HorariosAtencion.AddRangeAsync(franjas, ct);

    /// <inheritdoc />
    public async Task ReemplazarAsync(
        Guid lavaderoId, IReadOnlyList<FranjaAtencion> franjas, CancellationToken ct)
    {
        var vigentes = await _contexto.HorariosAtencion
            .Where(f => f.LavaderoId == lavaderoId && f.Activo)
            .ToListAsync(ct);

        foreach (var franja in vigentes)
        {
            franja.Desactivar();
        }

        // Dos guardados y no uno: el índice único está filtrado por «activo»,
        // así que una franja desactivada y su reemplazo pueden compartir
        // (lavadero, día, apertura). Con un solo SaveChanges EF no garantiza
        // que el UPDATE de baja se ejecute antes del INSERT y el alta chocaría
        // contra el índice. La transacción la abre quien llama.
        await _contexto.SaveChangesAsync(ct);

        await _contexto.HorariosAtencion.AddRangeAsync(franjas, ct);
        await _contexto.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task GuardarAsync(CancellationToken ct) =>
        await _contexto.SaveChangesAsync(ct);
}
