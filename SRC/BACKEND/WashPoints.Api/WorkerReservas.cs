using WashPoints.Application.Pagos;

namespace WashPoints.Api;

/// <summary>
/// Host periódico del worker de conciliación (SPECS/slice-01 T-21).
///
/// Es solo el "reloj": en cada tick abre un scope DI (un DbContext propio),
/// resuelve <see cref="RespaldoYExpiracionServicio"/> y ejecuta un tick. Toda
/// la lógica vive en el servicio, que se testea directamente.
/// </summary>
public sealed class WorkerReservas : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OpcionesPago _opciones;
    private readonly ILogger<WorkerReservas> _logger;

    public WorkerReservas(
        IServiceScopeFactory scopeFactory,
        OpcionesPago opciones,
        ILogger<WorkerReservas> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(_opciones.IntervaloWorkerSegundos);
        using var temporizador = new PeriodicTimer(intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un tick fallido no debe tumbar el worker: se registra y se
                // espera el próximo intervalo.
                _logger.LogError(ex, "El tick del worker de reservas falló.");
            }

            if (!await temporizador.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task EjecutarTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var servicio = scope.ServiceProvider.GetRequiredService<RespaldoYExpiracionServicio>();
        var resultado = await servicio.EjecutarTickAsync(ct);

        if (resultado.Expirados > 0 || resultado.Respaldados > 0 || resultado.Fallidos > 0)
        {
            _logger.LogInformation(
                "Worker de reservas: {Expirados} expirados, {Respaldados} respaldados, {Fallidos} fallidos.",
                resultado.Expirados, resultado.Respaldados, resultado.Fallidos);
        }
    }
}
