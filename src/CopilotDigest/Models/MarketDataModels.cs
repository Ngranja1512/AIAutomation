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

public enum InsiderTransactionKind
{
    /// <summary>Open-market purchase or sale of actual shares.</summary>
    OpenMarket,
    /// <summary>Derivative transaction: RSU vesting, option exercise, etc.</summary>
    Derivative,
}

public class InsiderTrade
{
    public string InsiderName { get; init; } = string.Empty;
    public string InsiderTitle { get; init; } = string.Empty;
    /// <summary>"A" = Acquired (buy/vest), "D" = Disposed (sell/exercise).</summary>
    public string AcquiredOrDisposed { get; init; } = string.Empty;
    public InsiderTransactionKind Kind { get; init; } = InsiderTransactionKind.OpenMarket;
    public decimal? Shares { get; init; }
    public decimal? PricePerShare { get; init; }
    public DateOnly? TransactionDate { get; init; }
}

public class InsiderDataSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxFilings { get; set; } = 5;
    /// <summary>
    /// Appears in the User-Agent header sent to SEC EDGAR.
    /// SEC policy requires a working contact address.
    /// </summary>
    public string ContactEmail { get; set; } = "contact@example.com";
}

public class FinancialDataSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Only fetch fundamentals when the topic has this many holdings or fewer.
    /// Guards the portfolio pipeline (29 holdings) from making unnecessary calls.
    /// </summary>
    public int MaxHoldingsForEnrichment { get; set; } = 3;

    public string QuoteSummaryUrl { get; set; } = "https://query1.finance.yahoo.com/v10/finance/quoteSummary/";

    /// <summary>Seed URL hit first to get a session cookie before requesting the crumb.</summary>
    public string CrumbSeedUrl { get; set; } = "https://fc.yahoo.com/";

    /// <summary>Endpoint that returns the plain-text crumb token (requires the session cookie).</summary>
    public string CrumbUrl { get; set; } = "https://query1.finance.yahoo.com/v1/test/getcrumb";

    /// <summary>Fallback endpoint — more permissive, provides market cap and valuation ratios.</summary>
    public string QuoteV7Url { get; set; } = "https://query1.finance.yahoo.com/v7/finance/quote";
}

public class AlphaVantageSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://www.alphavantage.co/query";
}

public record AnnualFinancials(int Year, decimal? Revenue, decimal? NetIncome);

public class FinancialSnapshot
{
    public string Ticker { get; init; } = string.Empty;
    /// <summary>True market capitalisation from summaryDetail (not enterprise value).</summary>
    public decimal? MarketCap { get; init; }
    public decimal? TrailingPE { get; init; }
    public decimal? ForwardPE { get; init; }
    public decimal? TrailingEps { get; init; }
    public decimal? ForwardEps { get; init; }
    public decimal? RevenueTTM { get; init; }
    public decimal? RevenueGrowthYoY { get; init; }
    public decimal? GrossMargins { get; init; }
    public decimal? OperatingMargins { get; init; }
    public decimal? ProfitMargins { get; init; }
    public decimal? FreeCashflow { get; init; }
    public decimal? TotalCash { get; init; }
    public decimal? TotalDebt { get; init; }
    /// <summary>Analyst consensus string, e.g. "buy", "hold", "sell" (from financialData).</summary>
    public string? AnalystConsensus { get; init; }
    /// <summary>Mean analyst recommendation on a 1.0 (strong buy) – 5.0 (sell) scale.</summary>
    public decimal? AnalystMean { get; init; }
    /// <summary>Next confirmed earnings date from Yahoo Finance calendarEvents.</summary>
    public DateOnly? NextEarningsDate { get; init; }
    public IReadOnlyList<AnnualFinancials> AnnualHistory { get; init; } = [];
}