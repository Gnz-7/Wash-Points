using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Application.Catalogo;

/// <summary>
/// Gestión de puestos de lavado (CU-05, SPECS/slice-04 T-44).
///
/// Escribe siempre por <see cref="IPuestoRepositorio"/> (un único flush). La
/// desactivación consulta <see cref="IOcupacionRepositorio.TieneTurnosVigentesAsync"/>
/// antes de tocar el puesto: es la regla D5, que impide liberar capacidad
/// todavía ocupada (RB-2). El reloj se inyecta para poder testear la ventana de
/// vigencia sin depender de la hora real.
/// </summary>
public sealed class GestionPuestosServicio
{
    private readonly IPuestoRepositorio _puestos;
    private readonly IOcupacionRepositorio _ocupacion;
    private readonly IConsultaCatalogo _catalogo;
    private readonly TimeProvider _reloj;

    public GestionPuestosServicio(
        IPuestoRepositorio puestos,
        IOcupacionRepositorio ocupacion,
        IConsultaCatalogo catalogo,
        TimeProvider reloj)
    {
        _puestos = puestos;
        _ocupacion = ocupacion;
        _catalogo = catalogo;
        _reloj = reloj;
    }

    public async Task<Puesto> CrearAsync(Guid lavaderoId, string nombre, CancellationToken ct)
    {
        // Puesto.Crear valida el nombre (ArgumentException -> 400).
        var puesto = Puesto.Crear(lavaderoId, nombre);

        await _puestos.AgregarAsync(puesto, ct);
        // El índice único se traduce a PuestoDuplicadoException en el repositorio.
        await _puestos.GuardarAsync(ct);

        return puesto;
    }

    public Task<IReadOnlyList<Puesto>> ListarAsync(Guid lavaderoId, CancellationToken ct) =>
        _puestos.ListarAsync(lavaderoId, ct);

    public async Task<Puesto> DesactivarAsync(Guid lavaderoId, Guid puestoId, CancellationToken ct)
    {
        var ahoraUtc = _reloj.GetUtcNow().UtcDateTime;

        if (await _ocupacion.TieneTurnosVigentesAsync(lavaderoId, puestoId, ahoraUtc, ct))
        {
            throw new PuestoConTurnosActivosException(
                "El puesto tiene turnos vigentes; no se puede desactivar.");
        }

        var puesto = await _puestos.ObtenerAsync(lavaderoId, puestoId, ct)
            ?? throw new PuestoNoEncontradoException(
                $"No existe el puesto {puestoId} en el lavadero {lavaderoId}.");

        puesto.Desactivar();
        await _puestos.GuardarAsync(ct);

        return puesto;
    }

    /// <summary>
    /// Reemplaza los tipos de lavado que atiende el puesto (slice-17, D-17.10,
    /// R-4). Una lista vacía deja el puesto atendiendo todos los servicios
    /// (D-17.7). Un tipo de otro lavadero o inactivo es indistinguible de
    /// inexistente y se rechaza antes de escribir nada.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> AsignarTiposAsync(
        Guid lavaderoId,
        Guid puestoId,
        IReadOnlyList<Guid> tiposLavadoIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tiposLavadoIds);

        _ = await _puestos.ObtenerAsync(lavaderoId, puestoId, ct)
            ?? throw new PuestoNoEncontradoException(
                $"No existe el puesto {puestoId} en el lavadero {lavaderoId}.");

        var distintos = tiposLavadoIds.Distinct().ToArray();

        foreach (var tipoLavadoId in distintos)
        {
            if (await _catalogo.ObtenerTipoLavadoAsync(lavaderoId, tipoLavadoId, ct) is null)
            {
                throw new TipoLavadoNoEncontradoException(
                    $"No existe el tipo de lavado {tipoLavadoId} en el lavadero {lavaderoId}.");
            }
        }

        await _puestos.ReemplazarTiposAsync(puestoId, distintos, ct);
        await _puestos.GuardarAsync(ct);

        return distintos;
    }

    public async Task<Puesto> ActivarAsync(Guid lavaderoId, Guid puestoId, CancellationToken ct)
    {
        var puesto = await _puestos.ObtenerAsync(lavaderoId, puestoId, ct)
            ?? throw new PuestoNoEncontradoException(
                $"No existe el puesto {puestoId} en el lavadero {lavaderoId}.");

        puesto.Activar();
        await _puestos.GuardarAsync(ct);

        return puesto;
    }
}
