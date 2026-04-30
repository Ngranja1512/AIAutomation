using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches recent macro and market news from Yahoo Finance RSS feeds.
/// No API key required — uses publicly available RSS endpoints.
/// </summary>
public partial class YahooFinanceNewsService : IMarketNewsService
{
    // Market-wide and commodity feeds for macro context.
    private static readonly string[] DefaultMacroFeedUrls =
    [
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=%5EGSPC&region=US&lang=en-US",   // S&P 500
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=%5EGDAXI&region=DE&lang=en-US",  // DAX / European equities
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=CL%3DF&region=US&lang=en-US",    // Crude oil
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=GC%3DF&region=US&lang=en-US",    // Gold
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=SI%3DF&region=US&lang=en-US",    // Silver
        "https://finance.yahoo.com/rss/topfinstories",                                         // Top finance stories
    ];

    // Per-stock feeds mapped to the portfolio holdings.
    // Uses the primary-listing ticker for each company to get English-language news.
    private static readonly string[] DefaultStockFeedUrls =
    [
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=GOOGL&region=US&lang=en-US",     // Alphabet        (ABEA)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=NVO&region=US&lang=en-US",        // Novo Nordisk    (NVO)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=O&region=US&lang=en-US",          // Realty Income   (RY6)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=BRK-B&region=US&lang=en-US",     // Berkshire B     (BRYN)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=MSFT&region=US&lang=en-US",       // Microsoft       (MSF)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=JD&region=US&lang=en-US",         // JD.com          (013A)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=NVDA&region=US&lang=en-US",       // NVIDIA          (NVD)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=1810.HK&region=HK&lang=en-US",   // Xiaomi          (3CP)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=VICI&region=US&lang=en-US",       // Vici Properties (1KN)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=MSTR&region=US&lang=en-US",       // MicroStrategy   (MIGA)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=CON.DE&region=DE&lang=en-US",    // Continental     (CON)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=XPEV&region=US&lang=en-US",       // XPeng           (8XP)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=BABA&region=US&lang=en-US",       // Alibaba         (BABA)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=EDP.LS&region=PT&lang=en-US",    // EDP SA          (EDP)
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=UNH&region=US&lang=en-US",        // UnitedHealth    (UNH)
    ];

    private readonly HttpClient _http;
    private readonly NewsSettings _settings;
    private readonly ILogger<YahooFinanceNewsService> _logger;

    public YahooFinanceNewsService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<YahooFinanceNewsService> logger)
    {
        _http = http;
        _settings = options.Value.News;
        _logger = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CopilotDigest/1.0");
        }
    }

    public async Task<IReadOnlyList<NewsItem>> GetMacroNewsAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            return [];
        }

        // When the user provides custom FeedUrls, use those exclusively.
        // Otherwise combine the default macro feeds and per-stock feeds.
        IEnumerable<(string Url, int MaxItems)> feeds = _settings.FeedUrls.Count > 0
            ? _settings.FeedUrls.Select(u => (u, _settings.MaxItemsPerFeed))
            : [
                .. DefaultMacroFeedUrls.Select(u => (u, _settings.MaxItemsPerFeed)),
                .. DefaultStockFeedUrls.Select(u => (u, _settings.MaxItemsPerStockFeed)),
              ];

        var allItems = new List<NewsItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (url, maxItems) in feeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var items = await FetchFeedAsync(url, maxItems, cancellationToken);
                foreach (var item in items)
                {
                    if (seen.Add(item.Title))
                    {
                        allItems.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch news feed: {Url}", url);
            }
        }

        return [.. allItems.OrderByDescending(i => i.PublishedAt ?? DateTimeOffset.MinValue)];
    }

    private async Task<List<NewsItem>> FetchFeedAsync(string url, int maxItems, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);

        var doc = XDocument.Parse(xml);
        var source = LabelFromUrl(url);

        return [.. doc.Descendants("item")
            .Take(maxItems)
            .Select(item => new NewsItem
            {
                Title = item.Element("title")?.Value.Trim() ?? string.Empty,
                Description = SanitiseDescription(item.Element("description")?.Value),
                Source = source,
                PublishedAt = ParsePubDate(item.Element("pubDate")?.Value),
            })
            .Where(i => !string.IsNullOrWhiteSpace(i.Title))];
    }

    private static string? SanitiseDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var clean = HtmlTagPattern().Replace(raw, " ").Trim();
        clean = MultipleSpaces().Replace(clean, " ");
        return clean.Length > 220 ? clean[..220] + "…" : clean;
    }

    private static string LabelFromUrl(string url) => url switch
    {
        var u when u.Contains("%5EGSPC", StringComparison.OrdinalIgnoreCase)       => "Yahoo Finance / S&P 500",
        var u when u.Contains("%5EGDAXI", StringComparison.OrdinalIgnoreCase)      => "Yahoo Finance / DAX",
        var u when u.Contains("CL%3DF", StringComparison.OrdinalIgnoreCase)        => "Yahoo Finance / Crude Oil",
        var u when u.Contains("GC%3DF", StringComparison.OrdinalIgnoreCase)        => "Yahoo Finance / Gold",
        var u when u.Contains("SI%3DF", StringComparison.OrdinalIgnoreCase)        => "Yahoo Finance / Silver",
        var u when u.Contains("topfinstories", StringComparison.OrdinalIgnoreCase) => "Yahoo Finance / Top Stories",
        _ => ExtractTickerLabel(url),
    };

    private static string ExtractTickerLabel(string url)
    {
        var match = TickerQueryParam().Match(url);
        return match.Success
            ? $"Yahoo Finance / {Uri.UnescapeDataString(match.Groups[1].Value)}"
            : "Yahoo Finance";
    }

    private static DateTimeOffset? ParsePubDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // RFC 822 format used by most RSS feeds: "Tue, 29 Apr 2026 17:30:00 +0000"
        if (DateTimeOffset.TryParseExact(
                value.Trim(),
                ["ddd, dd MMM yyyy HH:mm:ss zzz", "ddd, dd MMM yyyy HH:mm:ss Z"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return dt;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            return dt;
        }

        return null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"[?&]s=([^&]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TickerQueryParam();
}
