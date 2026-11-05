using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Application.Catalogo;

/// <summary>
/// Gestión del catálogo de tipos de lavado (CU-05, SPECS/slice-13 T-131).
///
/// Reglas que aplica:
/// - D-13.1: todo se acota al lavadero del administrador autenticado.
/// - D-13.3: la baja es lógica, nunca un DELETE.
/// - D-13.4: no se desactiva un tipo con turnos vigentes; el predicado de
///   vigencia vive en <see cref="IOcupacionRepositorio.TieneTurnosVigentesDelTipoAsync"/>
///   y el reloj se inyecta para poder fijarlo en los tests.
/// - D-13.6: editar no toca turnos ya creados; sólo cambia el catálogo.
/// - D-13.9: un tipo de otro lavadero responde como inexistente.
/// </summary>
public sealed class GestionTiposLavadoServicio
{
    private readonly ITipoLavadoRepositorio _tipos;
    private readonly IOcupacionRepositorio _ocupacion;
    private readonly TimeProvider _reloj;

    public GestionTiposLavadoServicio(
        ITipoLavadoRepositorio tipos,
        IOcupacionRepositorio ocupacion,
        TimeProvider reloj)
    {
        _tipos = tipos;
        _ocupacion = ocupacion;
        _reloj = reloj;
    }

    public async Task<TipoLavado> CrearAsync(
        Guid lavaderoId,
        string nombre,
        int duracionMinutos,
        decimal precioTotal,
        decimal precioSena,
        CancellationToken ct)
    {
        // TipoLavado.Crear valida nombre, duración y el rango de la seña
        // (D-13.2). Un rechazo no llega a persistir nada.
        var tipo = TipoLavado.Crear(
            lavaderoId, nombre, duracionMinutos, precioTotal, precioSena);

        await _tipos.AgregarAsync(tipo, ct);
        // El índice único (lavadero_id, nombre) se traduce a
        // TipoLavadoDuplicadoException en el repositorio.
        await _tipos.GuardarAsync(ct);

        return tipo;
    }

    public Task<IReadOnlyList<TipoLavado>> ListarAsync(Guid lavaderoId, CancellationToken ct) =>
        _tipos.ListarAsync(lavaderoId, ct);

    public async Task<TipoLavado> EditarAsync(
        Guid lavaderoId,
        Guid tipoId,
        string nombre,
        int duracionMinutos,
        decimal precioTotal,
        decimal precioSena,
        CancellationToken ct)
    {
        var tipo = await ObtenerAsync(lavaderoId, tipoId, ct);

        tipo.Editar(nombre, duracionMinutos, precioTotal, precioSena);
        await _tipos.GuardarAsync(ct);

        return tipo;
    }

    public async Task<TipoLavado> DesactivarAsync(
        Guid lavaderoId,
        Guid tipoId,
        CancellationToken ct)
    {
        // Primero se resuelve la existencia: un tipo ajeno o inexistente
        // responde 404 y no consulta la vigencia (D-13.9).
        var tipo = await ObtenerAsync(lavaderoId, tipoId, ct);

        var ahoraUtc = _reloj.GetUtcNow().UtcDateTime;

        if (await _ocupacion.TieneTurnosVigentesDelTipoAsync(lavaderoId, tipoId, ahoraUtc, ct))
        {
            throw new TipoLavadoConTurnosActivosException(
                "El tipo de lavado tiene turnos vigentes; no se puede desactivar.");
        }

        tipo.Desactivar();
        await _tipos.GuardarAsync(ct);

        return tipo;
    }

    public async Task<TipoLavado> ActivarAsync(
        Guid lavaderoId,
        Guid tipoId,
        CancellationToken ct)
    {
        var tipo = await ObtenerAsync(lavaderoId, tipoId, ct);

        tipo.Activar();
        await _tipos.GuardarAsync(ct);

        return tipo;
    }

    private async Task<TipoLavado> ObtenerAsync(
        Guid lavaderoId,
        Guid tipoId,
        CancellationToken ct) =>
        await _tipos.ObtenerAsync(lavaderoId, tipoId, ct)
        ?? throw new TipoLavadoNoEncontradoException(
            $"No existe el tipo de lavado {tipoId} en el lavadero {lavaderoId}.");
}
