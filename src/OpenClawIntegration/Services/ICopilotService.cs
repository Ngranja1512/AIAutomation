using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>
/// Generates an AI-powered summary for a given topic using the
/// GitHub Copilot chat-completions API.
/// </summary>
public interface ICopilotService
{
    Task<string> SummariseTopicAsync(Topic topic, CancellationToken cancellationToken = default);
}
