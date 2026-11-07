using WashPoints.Application.Reembolsos;
using WashPoints.Domain.Reembolsos;

namespace WashPoints.Api;

/// <summary>
/// Host periódico del worker de ejecución de reembolsos (SPECS/slice-15
/// T-157). Es sólo el reloj: en cada tick abre un scope DI (un DbContext
/// propio) y delega en <see cref="ProcesarReembolsosServicio"/>, que es lo que
/// se testea directamente.
/// </summary>
public sealed class WorkerReembolsos : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OpcionesReembolso _opciones;
    private readonly ILogger<WorkerReembolsos> _logger;

    public WorkerReembolsos(
        IServiceScopeFactory scopeFactory,
        OpcionesReembolso opciones,
        ILogger<WorkerReembolsos> logger)
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
                _logger.LogError(ex, "El tick del worker de reembolsos falló.");
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
        var servicio = scope.ServiceProvider.GetRequiredService<ProcesarReembolsosServicio>();
        var resultado = await servicio.EjecutarTickAsync(ct);

        if (resultado.Procesados > 0 || resultado.Reintentados > 0 || resultado.Fallidos > 0)
        {
            _logger.LogInformation(
                "Worker de reembolsos: {Procesados} procesados, {Reintentados} reintentados, {Fallidos} fallidos.",
                resultado.Procesados, resultado.Reintentados, resultado.Fallidos);
        }
    }
}
