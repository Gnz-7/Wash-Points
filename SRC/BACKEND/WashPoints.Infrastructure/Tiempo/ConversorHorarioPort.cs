using WashPoints.Domain.Tiempo;

namespace WashPoints.Infrastructure.Tiempo;

/// <summary>
/// Adaptador de <see cref="IConversorHorario"/> sobre <see cref="ConversorHorario"/>
/// (SPECS/slice-17, D-17.5).
///
/// Existe para que Application consuma la conversión por puerto sin referenciar
/// Infrastructure. Hay una sola zona horaria en el sistema, así que el adaptador
/// es sin estado y se registra como singleton.
/// </summary>
public sealed class ConversorHorarioPort : IConversorHorario
{
    /// <inheritdoc />
    public DateTime ALocal(DateTime instanteUtc) => ConversorHorario.ALocal(instanteUtc);

    /// <inheritdoc />
    public DateTime AUtc(DateTime horaLocal) => ConversorHorario.AUtc(horaLocal);
}
