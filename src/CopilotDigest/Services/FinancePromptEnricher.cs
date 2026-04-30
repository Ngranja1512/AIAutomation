using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Detects holdings embedded in a markdown portfolio prompt and prepends a live market snapshot
/// plus recent macro news headlines.
/// </summary>
public class FinancePromptEnricher : IFinancePromptEnricher
{
    private readonly IFreeMarketDataService _marketDataService;
    private readonly IMarketNewsService _newsService;
    private readonly ILogger<FinancePromptEnricher> _logger;

    public FinancePromptEnricher()
        : this(new EmptyMarketDataService(), new EmptyNewsService(), NullLogger<FinancePromptEnricher>.Instance)
    {
    }

    // Kept for backward compatibility (used by existing tests).
    public FinancePromptEnricher(
        IFreeMarketDataService marketDataService,
        ILogger<FinancePromptEnricher> logger)
        : this(marketDataService, new EmptyNewsService(), logger)
    {
    }

    public FinancePromptEnricher(
        IFreeMarketDataService marketDataService,
        IMarketNewsService newsService,
        ILogger<FinancePromptEnricher> logger)
    {
        _marketDataService = marketDataService;
        _newsService = newsService;
        _logger = logger;
    }

    public async Task<Topic> EnrichTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);

        if (string.IsNullOrWhiteSpace(topic.Prompt))
        {
            return topic;
        }

        var holdings = ParseHoldings(topic.Prompt);
        if (holdings.Count == 0)
        {
            return topic;
        }

        IReadOnlyList<MarketSnapshot> snapshots;
        try
        {
            snapshots = await _marketDataService.GetSnapshotsAsync(holdings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich finance prompt for topic {Topic}", topic.Name);
            return topic;
        }

        IReadOnlyList<NewsItem> newsItems;
        try
        {
            newsItems = await _newsService.GetMacroNewsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch macro news for topic {Topic}", topic.Name);
            newsItems = [];
        }

        if (snapshots.Count == 0 && newsItems.Count == 0)
        {
            return topic;
        }

        var prefix = new StringBuilder();
        if (snapshots.Count > 0)
        {
            prefix.AppendLine(BuildMarketDataSection(snapshots));
        }
        if (newsItems.Count > 0)
        {
            prefix.AppendLine();
            prefix.AppendLine(BuildNewsSection(newsItems));
        }

        return new Topic
        {
            Name = topic.Name,
            Description = topic.Description,
            Prompt = $"{prefix.ToString().TrimEnd()}\n\n{topic.Prompt}",
        };
    }

    private static IReadOnlyList<PortfolioHolding> ParseHoldings(string prompt)
    {
        var lines = prompt.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var holdings = new List<PortfolioHolding>();
        var inTable = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!inTable)
            {
                if (line.StartsWith("| Ticker", StringComparison.OrdinalIgnoreCase))
                {
                    inTable = true;
                }

                continue;
            }

            if (!line.StartsWith('|'))
            {
                break;
            }

            if (IsSeparatorRow(line))
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length < 5)
            {
                continue;
            }

            holdings.Add(new PortfolioHolding
            {
                Ticker = cells[0],
                Name = cells[1],
                Type = cells[2],
                Quantity = ParseDecimal(cells[3]),
                AverageBuyPrice = ParseDecimal(cells[4]),
            });
        }

        return holdings;
    }

    private static string BuildMarketDataSection(IReadOnlyList<MarketSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Live Market Data Snapshot");
        builder.AppendLine($"Generated at: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine("Source currencies are shown as returned by each upstream provider.");
        builder.AppendLine();

        foreach (var snapshot in snapshots)
        {
            builder.Append("- ");
            builder.Append(snapshot.Ticker);

            if (!string.IsNullOrWhiteSpace(snapshot.ProviderSymbol) &&
                !string.Equals(snapshot.ProviderSymbol, snapshot.Ticker, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" (");
                builder.Append(snapshot.ProviderSymbol);
                builder.Append(')');
            }

            builder.Append(": ");

            if (!snapshot.IsAvailable || snapshot.CurrentPrice is null)
            {
                builder.Append("live data unavailable");
                if (!string.IsNullOrWhiteSpace(snapshot.Note))
                {
                    builder.Append(" (");
                    builder.Append(snapshot.Note);
                    builder.Append(')');
                }

                builder.AppendLine(".");
                continue;
            }

            builder.Append(FormatDecimal(snapshot.CurrentPrice.Value));
            if (!string.IsNullOrWhiteSpace(snapshot.Currency))
            {
                builder.Append(' ');
                builder.Append(snapshot.Currency);
            }

            if (snapshot.DailyChangePercent is not null)
            {
                builder.Append(", 1d ");
                builder.Append(FormatPercent(snapshot.DailyChangePercent.Value));
            }

            if (snapshot.WeeklyChangePercent is not null)
            {
                builder.Append(IsCrypto(snapshot) ? ", 7d " : ", 5d ");
                builder.Append(FormatPercent(snapshot.WeeklyChangePercent.Value));
            }

            if (snapshot.AsOf is not null)
            {
                builder.Append(", as of ");
                builder.Append(snapshot.AsOf.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsSeparatorRow(string line)
    {
        foreach (var ch in line)
        {
            if (ch is not ('|' or '-' or ':' or ' '))
            {
                return false;
            }
        }

        return true;
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
    {
        var sign = value > 0 ? "+" : string.Empty;
        return $"{sign}{value.ToString("0.##", CultureInfo.InvariantCulture)}%";
    }

    private static bool IsCrypto(MarketSnapshot snapshot) =>
        string.Equals(snapshot.AssetType, "Crypto", StringComparison.OrdinalIgnoreCase);

    private static string BuildNewsSection(IReadOnlyList<NewsItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Recent Macro & Market News");
        builder.AppendLine($"Retrieved: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine();

        foreach (var item in items)
        {
            var timestamp = item.PublishedAt.HasValue
                ? item.PublishedAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                : "date unknown";
            var source = string.IsNullOrWhiteSpace(item.Source) ? "Yahoo Finance" : item.Source;

            builder.Append($"- [{timestamp} | {source}] ");
            builder.AppendLine(item.Title);

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                builder.AppendLine($"  {item.Description}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private sealed class EmptyMarketDataService : IFreeMarketDataService
    {
        public Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
            IReadOnlyList<PortfolioHolding> holdings,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MarketSnapshot>>([]);
        }
    }

    private sealed class EmptyNewsService : IMarketNewsService
    {
        public Task<IReadOnlyList<NewsItem>> GetMacroNewsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([]);
    }
}