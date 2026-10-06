using WashPoints.Domain.Ocupacion;
using WashPoints.Domain.Turnos;

namespace WashPoints.Application.Ocupacion;

/// <inheritdoc />
public sealed class OcupacionServicio : IOcupacionServicio
{
    private readonly IOcupacionRepositorio _repositorio;

    public OcupacionServicio(IOcupacionRepositorio repositorio) => _repositorio = repositorio;

    public async Task<Guid> ReservarAsync(Turno turno, CancellationToken ct)
    {
        if (turno.Estado != EstadoTurno.PendientePago)
        {
            throw new ArgumentException(
                "Solo se puede reservar un turno nuevo, en pendiente_pago.",
                nameof(turno));
        }

        await _repositorio.AgregarAsync(turno, ct);
        await _repositorio.GuardarAsync(ct);
        return turno.Id;
    }

    public async Task ConfirmarOcupacionAsync(Guid turnoId, CancellationToken ct)
    {
        var turno = await _repositorio.ObtenerAsync(turnoId, ct)
            ?? throw new KeyNotFoundException($"No existe el turno {turnoId}.");

        // RB-1: la transición a confirmado es la única forma de entrar al
        // predicado del EXCLUDE y, con él, de ocupar un puesto. Si otro turno
        // ya ocupa el rango, el motor rechaza el guardado y el repositorio lo
        // traduce a TurnoConflictoException (409 en la capa HTTP).
        turno.TransicionarA(EstadoTurno.Confirmado);

        await _repositorio.GuardarAsync(ct);
    }
}