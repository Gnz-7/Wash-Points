namespace WashPoints.Infrastructure.MercadoPago;

/// <summary>
/// Regla de arranque de la configuración de pagos (slice-15, D-15.8).
///
/// En Production, sin credenciales la seña no puede cobrarse (RB-1) y el
/// sistema no debe quedar arriba simulando que sí: se falla al arrancar. En
/// Development se permite arrancar sin claves para no bloquear el trabajo
/// local; la reserva responde 503 explícito.
/// </summary>
public static class ValidadorConfiguracionPagos
{
    public static void Validar(OpcionesMercadoPago opciones, bool esProduccion)
    {
        if (!esProduccion)
        {
            return;
        }

        var faltantes = opciones.ClavesFaltantes();
        if (faltantes.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Configuración de pagos incompleta en Production. " +
            $"Definí por variable de entorno: {string.Join(", ", faltantes)}. " +
            "Sin credenciales la seña no puede cobrarse (RB-1).");
    }
}
