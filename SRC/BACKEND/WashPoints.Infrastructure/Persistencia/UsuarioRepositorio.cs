using Microsoft.EntityFrameworkCore;
using Npgsql;
using WashPoints.Domain.Seguridad;

namespace WashPoints.Infrastructure.Persistencia;

/// <summary>
/// Implementación EF del puerto de credenciales (SPECS/slice-05 T-51/T-53).
/// <c>nombre_usuario</c> es único, por eso alcanza una única fila.
///
/// El alta (slice-08, D-09.2/D-09.3) persiste en el acto dentro de la
/// transacción que abre <c>IUnidadDeTrabajo</c>: si el nombre ya está tomado,
/// el rollback de esa transacción se lleva también el lavadero o el cliente
/// creado en el mismo bloque (nunca quedan huérfanos).
/// </summary>
public sealed class UsuarioRepositorio : IUsuarioRepositorio
{
    private const string SqlStateUniqueViolation = "23505";

    private readonly WashPointsDbContext _contexto;

    public UsuarioRepositorio(WashPointsDbContext contexto) => _contexto = contexto;

    public async Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario, CancellationToken ct) =>
        await _contexto.Usuarios
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.NombreUsuario == nombreUsuario, ct);

    public Task<bool> ExisteAsync(string nombreUsuario, CancellationToken ct) =>
        _contexto.Usuarios.AnyAsync(u => u.NombreUsuario == nombreUsuario, ct);

    public async Task AgregarAsync(Usuario usuario, CancellationToken ct)
    {
        await _contexto.Usuarios.AddAsync(usuario, ct);

        try
        {
            await _contexto.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException postgres &&
                  postgres.SqlState == SqlStateUniqueViolation)
        {
            // El índice UNIQUE(nombre_usuario) es la última palabra: el
            // chequeo previo (ExisteAsync) puede perder una carrera.
            throw new UsuarioYaExisteException(
                "Ya existe una cuenta con ese nombre de usuario.", ex);
        }
    }
}
