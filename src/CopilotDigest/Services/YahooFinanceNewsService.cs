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
    // Sensible defaults covering the main exposure themes in a typical diversified portfolio.
    private static readonly string[] DefaultFeedUrls =
    [
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=%5EGSPC&region=US&lang=en-US",   // S&P 500
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=%5EGDAXI&region=DE&lang=en-US",  // DAX / European equities
        "https://feeds.finance.yahoo.com/rss/2.0/headline?s=CL%3DF&region=US&lang=en-US",    // Crude oil
        "https://finance.yahoo.com/rss/topfinstories",                                         // Top finance stories
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

        var feedUrls = _settings.FeedUrls.Count > 0
            ? _settings.FeedUrls
            : [.. DefaultFeedUrls];

        var allItems = new List<NewsItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in feedUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var items = await FetchFeedAsync(url, cancellationToken);
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

    private async Task<List<NewsItem>> FetchFeedAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);

        var doc = XDocument.Parse(xml);
        var source = LabelFromUrl(url);

        return [.. doc.Descendants("item")
            .Take(_settings.MaxItemsPerFeed)
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
        var u when u.Contains("%5EGSPC", StringComparison.OrdinalIgnoreCase)  => "Yahoo Finance / S&P 500",
        var u when u.Contains("%5EGDAXI", StringComparison.OrdinalIgnoreCase) => "Yahoo Finance / DAX",
        var u when u.Contains("CL%3DF", StringComparison.OrdinalIgnoreCase)   => "Yahoo Finance / Crude Oil",
        var u when u.Contains("topfinstories", StringComparison.OrdinalIgnoreCase) => "Yahoo Finance / Top Stories",
        _ => "Yahoo Finance",
    };

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
}
