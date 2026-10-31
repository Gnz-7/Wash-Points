using System.Collections.Concurrent;
using WashPoints.Domain.Puertos;

namespace WashPoints.UnitTests.Fakes;

/// <summary>
/// Doble simulado de <see cref="IPasarelaPagos"/> (SPECS/slice-01 T-12).
///
/// Permite scriptear respuestas y simular caídas sin servidor real. Es el
/// guardián de RB-1 en los tests: por defecto **nunca** devuelve
/// <see cref="EstadoVerificacionPago.Aprobado"/>; solo lo hace si el test lo
/// scripteó explícitamente. Un turno se confirma únicamente cuando el test
/// decidió que la pasarela lo aprobara, igual que en producción solo se
/// confirma con el webhook verificado.
///
/// Vive en el proyecto de tests unitarios; los tests de integración lo
/// referencian y lo inyectan vía DI para sustituir a la implementación real.
/// </summary>
public sealed class PasarelaPagosFakeSimulada : IPasarelaPagos
{
    private readonly ConcurrentDictionary<string, ResultadoVerificacionPago> _libreto = new();
    private readonly List<PreferenciaPago> _preferencias = new();
    private readonly object _candado = new();
    private int _secuencia;

    /// <summary>
    /// Cuando está activo, toda llamada falla con
    /// <see cref="PasarelaNoDisponibleException"/>. Modela el timeout/red caída
    /// que el worker debe tolerar sin retrasar la expiración local (T-21).
    /// </summary>
    public bool EnModoCaida { get; set; }

    /// <summary>Preferencias que se pidieron, en orden de llegada.</summary>
    public IReadOnlyList<PreferenciaPago> PreferenciasCreadas => _preferencias;

    /// <summary>Cantidad de veces que se consultó un pago (para asserts de sondeo).</summary>
    public int VerificacionesRealizadas => Volatile.Read(ref _verificaciones);

    private int _verificaciones;

    /// <summary>Fija la respuesta que devolverá <see cref="VerificarPagoAsync"/> para un id externo.</summary>
    public void ScriptearVerificacion(string pagoIdExterno, ResultadoVerificacionPago resultado) =>
        _libreto[pagoIdExterno] = resultado;

    /// <summary>Fuerza el id externo que devolverá la próxima <see cref="CrearPreferenciaAsync"/>.</summary>
    public void ScriptearProximaPreferencia(string id) => _proximoIdForzado = id;

    private string? _proximoIdForzado;

    public Task<PreferenciaCreada> CrearPreferenciaAsync(PreferenciaPago preferencia, CancellationToken ct)
    {
        LanzarSiCayo();

        // El fake es un singleton inyectado en los tests; en las pruebas de
        // concurrencia (T-20) varias requests lo llaman a la vez.
        lock (_candado)
        {
            _preferencias.Add(preferencia);

            var id = _proximoIdForzado ?? $"pref-{++_secuencia}";
            _proximoIdForzado = null;

            // La URL imita el checkout real: el cliente nunca la inspecciona,
            // solo la abre. Los tests comparan el prefijo.
            return Task.FromResult(new PreferenciaCreada(id, $"https://checkout.test/{id}"));
        }
    }

    public Task<ResultadoVerificacionPago> VerificarPagoAsync(string pagoIdExterno, CancellationToken ct)
    {
        Interlocked.Increment(ref _verificaciones);
        LanzarSiCayo();

        var resultado = _libreto.TryGetValue(pagoIdExterno, out var scripteado)
            ? scripteado
            : ResultadoVerificacionPago.Pendiente();

        return Task.FromResult(resultado);
    }

    private void LanzarSiCayo()
    {
        if (EnModoCaida)
        {
            throw new PasarelaNoDisponibleException("La pasarela simulada está caída.");
        }
    }
}
