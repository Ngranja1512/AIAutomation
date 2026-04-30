using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches live market snapshots for holdings using free upstream sources.
/// </summary>
public interface IFreeMarketDataService
{
    Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
        IReadOnlyList<PortfolioHolding> holdings,
        CancellationToken cancellationToken = default);
}