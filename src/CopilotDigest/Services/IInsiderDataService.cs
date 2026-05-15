using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches recent insider transactions (Form 4 filings) for a given US-listed ticker.
/// </summary>
public interface IInsiderDataService
{
    Task<IReadOnlyList<InsiderTrade>> GetRecentTradesAsync(
        string ticker,
        CancellationToken cancellationToken = default);
}
