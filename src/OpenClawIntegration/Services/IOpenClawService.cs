using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>
/// Orchestrates topic research via the OpenClaw agent platform,
/// optionally falling back to direct Copilot calls.
/// </summary>
public interface IOpenClawService
{
    /// <summary>
    /// Runs the OpenClaw agent for the given topic and returns a summary.
    /// </summary>
    Task<SummaryResult> ResearchTopicAsync(Topic topic, CancellationToken cancellationToken = default);
}
