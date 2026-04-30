using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches recent macro and market news headlines from RSS feeds.
/// </summary>
public interface IMarketNewsService
{
    Task<IReadOnlyList<NewsItem>> GetMacroNewsAsync(CancellationToken cancellationToken = default);
}
