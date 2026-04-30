namespace CopilotDigest.Models;

public class MarketDataSettings
{
    public bool Enabled { get; set; } = true;

    public string YahooChartUrl { get; set; } = "https://query1.finance.yahoo.com/v8/finance/chart/";

    public string CoinGeckoMarketsUrl { get; set; } = "https://api.coingecko.com/api/v3/coins/markets";

    public List<TickerMapping> SymbolMappings { get; set; } = [];
}

public class NewsSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum headlines per macro/index RSS feed.</summary>
    public int MaxItemsPerFeed { get; set; } = 5;

    /// <summary>Maximum headlines per individual stock RSS feed.</summary>
    public int MaxItemsPerStockFeed { get; set; } = 3;

    /// <summary>RSS feed URLs to poll. When non-empty, overrides all built-in feeds.</summary>
    public List<string> FeedUrls { get; set; } = [];
}

public class NewsItem
{
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Source { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}

public class TickerMapping
{
    public string DisplayTicker { get; set; } = string.Empty;

    public string? YahooSymbol { get; set; }

    public string? CoinGeckoId { get; set; }
}

public class PortfolioHolding
{
    public string Ticker { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public decimal? Quantity { get; init; }

    public decimal? AverageBuyPrice { get; init; }
}

public class MarketSnapshot
{
    public string Ticker { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string AssetType { get; init; } = string.Empty;

    public string ProviderSymbol { get; init; } = string.Empty;

    public string? Currency { get; init; }

    public decimal? CurrentPrice { get; init; }

    public decimal? DailyChangePercent { get; init; }

    public decimal? WeeklyChangePercent { get; init; }

    public DateTimeOffset? AsOf { get; init; }

    public bool IsAvailable { get; init; }

    public string? Note { get; init; }
}