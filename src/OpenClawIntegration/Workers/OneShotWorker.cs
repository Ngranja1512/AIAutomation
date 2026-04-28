using OpenClawIntegration.Services;

namespace OpenClawIntegration.Workers;

/// <summary>
/// Runs a single summary cycle immediately and then requests host shutdown.
/// Used in GitHub Actions (one-shot / <c>--run-once</c> mode).
/// </summary>
public class OneShotWorker : BackgroundService
{
    private readonly SummaryWorker _summaryWorker;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<OneShotWorker> _logger;

    public OneShotWorker(
        IOpenClawService openClawService,
        IWhatsAppService whatsAppService,
        Microsoft.Extensions.Options.IOptions<Models.AppSettings> options,
        IHostApplicationLifetime lifetime,
        ILogger<OneShotWorker> logger,
        ILogger<SummaryWorker> summaryWorkerLogger)
    {
        _summaryWorker = new SummaryWorker(openClawService, whatsAppService, options, summaryWorkerLogger);
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("One-shot mode: running summary cycle.");
            await _summaryWorker.RunAsync(stoppingToken);
            _logger.LogInformation("One-shot mode: summary cycle complete. Shutting down.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "One-shot mode: summary cycle failed.");
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
