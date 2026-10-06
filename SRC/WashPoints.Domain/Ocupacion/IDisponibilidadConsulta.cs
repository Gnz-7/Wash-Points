namespace WashPoints.Domain.Ocupacion;

/// <summary>
/// Lectura de disponibilidad de un lavadero (US-C05 CA-5.1).
///
/// Es la mitad de RB-2: reservas y demanda espontánea llegaron a la misma
/// tabla, de modo que esta consulta las ve a ambas y no se puede divergir. Solo
/// devuelve turnos que ocupan puesto (<c>confirmado</c>/<c>en_curso</c>); los
/// <c>pendiente_pago</c> están fuera del predicado y no aparecen.
///
/// El rango de consulta <paramref name="desdeUtc"/>/<paramref name="hastaUtc"/>
/// se expresa en UTC en el contrato; la implementación lo traduce a la hora
/// local del lavadero para compararlo contra el <c>tsrange</c> (ARCHITECTURE
/// §10.6). Mismo distingo que el del guardado: UTC en el dominio, local en la
/// base.
/// </summary>
public interface IDisponibilidadConsulta
{
    Task<IReadOnlyList<IntervaloOcupado>> ObtenerOcupadosAsync(
        Guid lavaderoId,
        DateTime desdeUtc,
        DateTime hastaUtc,
        CancellationToken ct);
}