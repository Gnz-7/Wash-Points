using WashPoints.Application.Agenda;

namespace WashPoints.Api;

/// <summary>
/// Host periódico del cierre de agenda (SPECS/slice-04 T-47), modelado sobre
/// <see cref="WorkerReservas"/>: en cada tick abre un scope DI propio y delega
/// la regla en <see cref="CierreAgendaServicio"/>.
///
/// Espera un intervalo antes del primer tick: el cierre por ventana no es
/// sensible al arranque y así se evita cerrar en bloque turnos vencidos apenas
/// levanta el proceso.
/// </summary>
public sealed class WorkerAgenda : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OpcionesAgenda _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<WorkerAgenda> _logger;

    public WorkerAgenda(
        IServiceScopeFactory scopeFactory,
        OpcionesAgenda opciones,
        TimeProvider reloj,
        ILogger<WorkerAgenda> logger)
    {
        _scopeFactory = scopeFactory;
        _opciones = opciones;
        _reloj = reloj;
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
                if (!await temporizador.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }

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
                _logger.LogError(ex, "El tick del worker de agenda falló.");
            }
        }
    }

    private async Task EjecutarTickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var servicio = scope.ServiceProvider.GetRequiredService<CierreAgendaServicio>();
        var cerrados = await servicio.EjecutarTickAsync(_reloj.GetUtcNow().UtcDateTime, ct);

        if (cerrados > 0)
        {
            _logger.LogInformation(
                "Worker de agenda: {Cerrados} turnos marcados como no_presentado.", cerrados);
        }
    }
}
