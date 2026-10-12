using WashPoints.Application.Pagos;

namespace WashPoints.Api;

/// <summary>
/// Host periódico del publicador de la outbox (SPECS/slice-03 T-38).
///
/// Es solo el "reloj": en cada tick abre un scope DI (un <c>DbContext</c>
/// propio), resuelve <see cref="PublicadorOutbox"/> y bombea el lote pendiente.
/// La lógica vive en el publicador, que se testea directamente.
/// </summary>
public sealed class PublicadorOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OpcionesPago _opciones;
    private readonly ILogger<PublicadorOutboxWorker> _logger;

    public PublicadorOutboxWorker(
        IServiceScopeFactory scopeFactory,
        OpcionesPago opciones,
        ILogger<PublicadorOutboxWorker> logger)
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
                using var scope = _scopeFactory.CreateScope();
                var publicador = scope.ServiceProvider.GetRequiredService<PublicadorOutbox>();
                await publicador.PublicarPendientesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un tick fallido no debe tumbar el worker: se registra y se
                // reintenta en el próximo intervalo (la outbox es la cola).
                _logger.LogError(ex, "El tick del publicador de outbox falló.");
            }

            if (!await temporizador.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
