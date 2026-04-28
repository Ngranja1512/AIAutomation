using Cronos;
using Microsoft.Extensions.Options;
using OpenClawIntegration.Models;
using OpenClawIntegration.Services;

namespace OpenClawIntegration.Workers;

/// <summary>
/// Background worker that runs on a configurable cron schedule.
/// On each tick it calls the OpenClaw/Copilot service for every configured
/// topic and dispatches the summaries to WhatsApp.
/// </summary>
public class SummaryWorker : BackgroundService
{
    private readonly IOpenClawService _openClawService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly AppSettings _settings;
    private readonly ILogger<SummaryWorker> _logger;

    public SummaryWorker(
        IOpenClawService openClawService,
        IWhatsAppService whatsAppService,
        IOptions<AppSettings> options,
        ILogger<SummaryWorker> logger)
    {
        _openClawService = openClawService;
        _whatsAppService = whatsAppService;
        _settings = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SummaryWorker started. Cron: {Cron}, Topics: {Topics}",
            _settings.Scheduler.CronExpression,
            string.Join(", ", _settings.Topics.Select(t => t.Name)));

        var expression = CronExpression.Parse(
            _settings.Scheduler.CronExpression,
            CronFormat.Standard);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var next = expression.GetNextOccurrence(now, TimeZoneInfo.Utc);

            if (next is null)
            {
                _logger.LogWarning("Cron expression produced no next occurrence. Worker stopping.");
                break;
            }

            var delay = next.Value - now;
            _logger.LogInformation("Next run scheduled for {Next} (in {Delay:hh\\:mm\\:ss})", next, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunAsync(stoppingToken);
        }

        _logger.LogInformation("SummaryWorker stopped.");
    }

    /// <summary>
    /// Executes a single summary cycle immediately (used by the worker loop
    /// and can be triggered directly in one-shot / CI mode).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting summary cycle at {Time}", DateTimeOffset.UtcNow);

        if (_settings.Topics.Count == 0)
        {
            _logger.LogWarning("No topics configured. Add topics to appsettings.json.");
            return;
        }

        var tasks = _settings.Topics
            .Select(topic => _openClawService.ResearchTopicAsync(topic, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        var successful = results.Count(r => r.IsSuccess);
        var failed = results.Length - successful;

        _logger.LogInformation(
            "Summary cycle complete. Successful: {Success}, Failed: {Failed}",
            successful, failed);

        await _whatsAppService.SendSummariesAsync(results, cancellationToken);
    }
}
