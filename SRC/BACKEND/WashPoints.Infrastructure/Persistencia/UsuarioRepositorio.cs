using Microsoft.EntityFrameworkCore;
using WashPoints.Domain.Seguridad;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de credenciales (SPECS/slice-05 T-51/T-53).
/// <c>nombre_usuario</c> es único, por eso alcanza una única fila.
/// </summary>
public sealed class UsuarioRepositorio : IUsuarioRepositorio
{
    private readonly WashPointsDbContext _contexto;

    public UsuarioRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken ct) =>
        await _contexto.Usuarios
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NombreUsuario == nombreUsuario, ct);
}
