using System.Globalization;
using WashPoints.Application.Catalogo;
using WashPoints.Domain.Catalogo;
using WashPoints.Domain.Ocupacion;

namespace WashPoints.Application.Horarios;

/// <summary>
/// Horarios de atención del lavadero (SPECS/slice-17 §4.1/§4.2, T-178, R-1).
///
/// La lectura devuelve las franjas activas ordenadas por día y apertura. La
/// escritura reemplaza el conjunto completo dentro de una transacción: el panel
/// manda la semana entera y no queda un estado intermedio mientras se edita.
/// Toda validación de forma vive acá —día, formato, orden y superposición— y
/// se traduce a <see cref="FranjaInvalidaException"/>; las reglas de reserva
/// (anticipación, horizonte) no se mezclan con este ABM.
/// </summary>
public sealed class GestionHorariosServicio
{
    /// <summary>Máximo de franjas admitidas por día (D-17.11).</summary>
    public const int MaximoFranjasPorDia = 6;

    private const string FormatoHora = "HH:mm";

    private readonly IHorarioRepositorio _horarios;
    private readonly IConsultaCatalogo _catalogo;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;
    private readonly TimeProvider _reloj;

    public GestionHorariosServicio(
        IHorarioRepositorio horarios,
        IConsultaCatalogo catalogo,
        IUnidadDeTrabajo unidadDeTrabajo,
        TimeProvider reloj)
    {
        _horarios = horarios ?? throw new ArgumentNullException(nameof(horarios));
        _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
        _unidadDeTrabajo = unidadDeTrabajo ?? throw new ArgumentNullException(nameof(unidadDeTrabajo));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
    }

    /// <summary>Franjas activas del lavadero, ordenadas por día y apertura.</summary>
    public async Task<IReadOnlyList<FranjaAtencion>> ObtenerAsync(
        Guid lavaderoId, CancellationToken ct = default)
    {
        await AsegurarLavaderoAsync(lavaderoId, ct);
        return await _horarios.ListarAsync(lavaderoId, ct);
    }

    /// <summary>
    /// Reemplaza la semana completa del lavadero. Una lista vacía lo deja
    /// cerrado, que es un estado válido y advertido en el panel (D-17.11).
    /// </summary>
    public async Task<IReadOnlyList<FranjaAtencion>> ReemplazarAsync(
        Guid lavaderoId,
        IReadOnlyList<FranjaSolicitada> solicitadas,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitadas);

        await AsegurarLavaderoAsync(lavaderoId, ct);

        var creadoEn = _reloj.GetUtcNow().UtcDateTime;
        var nuevas = Validar(solicitadas)
            .Select(franja => FranjaAtencion.Crear(
                lavaderoId, franja.DiaSemana, franja.Apertura, franja.Cierre, creadoEn))
            .ToArray();

        await _unidadDeTrabajo.EjecutarEnTransaccionAsync(
            async token =>
            {
                await _horarios.ReemplazarAsync(lavaderoId, nuevas, token);
                return true;
            },
            ct);

        return nuevas;
    }

    /// <summary>
    /// Valida forma y coherencia de la semana. Devuelve las franjas ya
    /// parseadas y ordenadas por día y apertura.
    /// </summary>
    private static IReadOnlyList<(int DiaSemana, TimeOnly Apertura, TimeOnly Cierre)> Validar(
        IReadOnlyList<FranjaSolicitada> solicitadas)
    {
        var parseadas = new List<(int DiaSemana, TimeOnly Apertura, TimeOnly Cierre)>(
            solicitadas.Count);

        foreach (var franja in solicitadas)
        {
            if (franja.DiaSemana is < 1 or > 7)
            {
                throw new FranjaInvalidaException($"El día {franja.DiaSemana} no es válido.");
            }

            var apertura = ParsearHora(franja.Apertura, "apertura");
            var cierre = ParsearHora(franja.Cierre, "cierre");

            if (apertura >= cierre)
            {
                throw new FranjaInvalidaException("La apertura debe ser anterior al cierre.");
            }

            parseadas.Add((franja.DiaSemana, apertura, cierre));
        }

        foreach (var dia in parseadas.GroupBy(f => f.DiaSemana))
        {
            var delDia = dia.OrderBy(f => f.Apertura).ToArray();

            if (delDia.Length > MaximoFranjasPorDia)
            {
                throw new FranjaInvalidaException(
                    $"El día {dia.Key} admite como máximo {MaximoFranjasPorDia} franjas.");
            }

            for (var i = 1; i < delDia.Length; i++)
            {
                if (delDia[i].Apertura < delDia[i - 1].Cierre)
                {
                    throw new FranjaInvalidaException(
                        $"Las franjas del día {dia.Key} se superponen.");
                }
            }
        }

        return parseadas
            .OrderBy(f => f.DiaSemana)
            .ThenBy(f => f.Apertura)
            .ToArray();
    }

    private static TimeOnly ParsearHora(string valor, string campo)
    {
        if (!TimeOnly.TryParseExact(
                valor,
                FormatoHora,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var hora))
        {
            throw new FranjaInvalidaException(
                $"La {campo} '{valor}' no tiene formato HH:mm.");
        }

        return hora;
    }

    /// <summary>
    /// Un lavadero inexistente se trata como tal antes de tocar las franjas:
    /// el endpoint responde 404 y no se crea configuración huérfana.
    /// </summary>
    private async Task AsegurarLavaderoAsync(Guid lavaderoId, CancellationToken ct)
    {
        if (await _catalogo.ObtenerCatalogoAsync(lavaderoId, ct) is null)
        {
            throw new LavaderoNoEncontradoException(
                $"No existe el lavadero {lavaderoId}.");
        }
    }
}
