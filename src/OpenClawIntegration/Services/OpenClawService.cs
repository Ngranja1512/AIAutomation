using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>
/// Orchestrates topic research by calling the GitHub Copilot API
/// and wrapping the result in a <see cref="SummaryResult"/>.
/// </summary>
public class ResearchService : IResearchService
{
    private readonly ICopilotService _copilotService;
    private readonly ILogger<ResearchService> _logger;

    public ResearchService(
        ICopilotService copilotService,
        ILogger<ResearchService> logger)
    {
        _copilotService = copilotService;
        _logger = logger;
    }

    public async Task<SummaryResult> ResearchTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Requesting summary for topic: {Topic}", topic.Name);
            var summary = await _copilotService.SummariseTopicAsync(topic, cancellationToken);

            return new SummaryResult
            {
                TopicName = topic.Name,
                Summary = summary,
                GeneratedAt = DateTime.UtcNow,
                IsSuccess = true,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to research topic: {Topic}", topic.Name);
            return new SummaryResult
            {
                TopicName = topic.Name,
                GeneratedAt = DateTime.UtcNow,
                IsSuccess = false,
                ErrorMessage = ex.Message,
            };
        }
    }
}
