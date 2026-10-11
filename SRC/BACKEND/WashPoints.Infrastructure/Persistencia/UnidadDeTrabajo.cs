using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Transacción de base para la reserva (SPECS/slice-01 T-13, CA-S1.3).
///
/// El <see cref="WashPointsDbContext"/> es scoped: una transacción == un
/// request. El callback corre dentro de ella; si lanza, se revierte y se limpia
/// el ChangeTracker para que un intento fallido no deje entidades colgadas en
/// el contexto (relevante para la reconsulta de T-14).
/// </summary>
public sealed class UnidadDeTrabajo : IUnidadDeTrabajo
{
    private readonly WashPointsDbContext _contexto;

    public UnidadDeTrabajo(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<T> EjecutarEnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> trabajo,
        CancellationToken ct)
    {
        await using var transaccion = await _contexto.Database.BeginTransactionAsync(ct);

        try
        {
            var resultado = await trabajo(ct);
            await transaccion.CommitAsync(ct);
            return resultado;
        }
        catch
        {
            await transaccion.RollbackAsync(ct);
            _contexto.ChangeTracker.Clear();
            throw;
        }
    }
}
