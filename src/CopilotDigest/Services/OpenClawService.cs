using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Orchestrates topic research by calling the GitHub Copilot API
/// and wrapping the result in a <see cref="SummaryResult"/>.
/// </summary>
public class ResearchService : IResearchService
{
    private readonly ICopilotService _copilotService;
    private readonly IFinancePromptEnricher _financePromptEnricher;
    private readonly ILogger<ResearchService> _logger;

    public ResearchService(
        ICopilotService copilotService,
        ILogger<ResearchService> logger)
        : this(copilotService, new FinancePromptEnricher(), logger)
    {
    }

    public ResearchService(
        ICopilotService copilotService,
        IFinancePromptEnricher financePromptEnricher,
        ILogger<ResearchService> logger)
    {
        _copilotService = copilotService;
        _financePromptEnricher = financePromptEnricher;
        _logger = logger;
    }

    public async Task<SummaryResult> ResearchTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Requesting summary for topic: {Topic}", topic.Name);
            var enrichedTopic = await _financePromptEnricher.EnrichTopicAsync(topic, cancellationToken);
            var summary = await _copilotService.SummariseTopicAsync(enrichedTopic, cancellationToken);

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
