using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Builds an enriched topic prompt with any live finance context that is available.
/// </summary>
public interface IFinancePromptEnricher
{
    Task<Topic> EnrichTopicAsync(Topic topic, CancellationToken cancellationToken = default);
}