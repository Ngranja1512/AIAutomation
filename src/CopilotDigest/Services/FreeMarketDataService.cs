using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Resolves live snapshots from free sources. Yahoo Finance is used for equities,
/// ETFs, and commodities; CoinGecko is used for crypto assets.
/// </summary>
public class FreeMarketDataService : IFreeMarketDataService
{
    private static readonly IReadOnlyDictionary<string, string> DefaultCoinGeckoIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BTC"] = "bitcoin",
            ["ETH"] = "ethereum",
        };

    private readonly HttpClient _http;
    private readonly MarketDataSettings _settings;
    private readonly IReadOnlyDictionary<string, TickerMapping> _tickerMappings;
    private readonly ILogger<FreeMarketDataService> _logger;

    public FreeMarketDataService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<FreeMarketDataService> logger)
    {
        _http = http;
        _settings = options.Value.MarketData;
        _logger = logger;
        _tickerMappings = _settings.SymbolMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.DisplayTicker))
            .GroupBy(mapping => mapping.DisplayTicker.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CopilotDigest/1.0");
        }
    }

    public async Task<IReadOnlyList<MarketSnapshot>> GetSnapshotsAsync(
        IReadOnlyList<PortfolioHolding> holdings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(holdings);

        if (!_settings.Enabled || holdings.Count == 0)
        {
            return [];
        }

        var snapshots = new List<MarketSnapshot>(holdings.Count);

        foreach (var holding in holdings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MarketSnapshot snapshot;
            try
            {
                snapshot = IsCrypto(holding)
                    ? await GetCryptoSnapshotAsync(holding, cancellationToken)
                    : await GetYahooSnapshotAsync(holding, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch market data for {Ticker}", holding.Ticker);
                snapshot = CreateUnavailableSnapshot(holding, holding.Ticker, "provider error");
            }

            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private async Task<MarketSnapshot> GetYahooSnapshotAsync(
        PortfolioHolding holding,
        CancellationToken cancellationToken)
    {
        var providerSymbol = ResolveYahooSymbol(holding);
        if (string.IsNullOrWhiteSpace(providerSymbol))
        {
            return CreateUnavailableSnapshot(holding, holding.Ticker, "configure a Yahoo symbol mapping");
        }

        var requestUri = BuildYahooChartUri(providerSymbol);
        using var response = await _http.GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CreateUnavailableSnapshot(
                holding,
                providerSymbol,
                $"Yahoo Finance returned {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!TryGetYahooResult(document.RootElement, out var result, out var errorMessage))
        {
            return CreateUnavailableSnapshot(holding, providerSymbol, errorMessage);
        }

        var meta = result.GetProperty("meta");
        var currentPrice = TryGetDecimal(meta, "regularMarketPrice");
        if (currentPrice is null)
        {
            return CreateUnavailableSnapshot(holding, providerSymbol, "missing current price");
        }

        var previousClose = TryGetDecimal(meta, "previousClose");
        var asOf = TryGetUnixTime(meta, "regularMarketTime");

        return new MarketSnapshot
        {
            Ticker = holding.Ticker,
            Name = holding.Name,
            AssetType = holding.Type,
            ProviderSymbol = providerSymbol,
            Currency = TryGetString(meta, "currency"),
            CurrentPrice = currentPrice,
            DailyChangePercent = CalculatePercentChange(currentPrice, previousClose),
            WeeklyChangePercent = TryGetYahooWeeklyChangePercent(result),
            AsOf = asOf,
            IsAvailable = true,
        };
    }

    private async Task<MarketSnapshot> GetCryptoSnapshotAsync(
        PortfolioHolding holding,
        CancellationToken cancellationToken)
    {
        var providerSymbol = ResolveCoinGeckoId(holding);
        if (string.IsNullOrWhiteSpace(providerSymbol))
        {
            return CreateUnavailableSnapshot(holding, holding.Ticker, "configure a CoinGeckoId mapping");
        }

        var requestUri = BuildCoinGeckoMarketsUri(providerSymbol);
        using var response = await _http.GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return CreateUnavailableSnapshot(
                holding,
                providerSymbol,
                $"CoinGecko returned {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return CreateUnavailableSnapshot(holding, providerSymbol, "CoinGecko returned no data");
        }

        var market = document.RootElement[0];
        var currentPrice = TryGetDecimal(market, "current_price");
        if (currentPrice is null)
        {
            return CreateUnavailableSnapshot(holding, providerSymbol, "missing current price");
        }

        DateTimeOffset? asOf = null;
        var lastUpdated = TryGetString(market, "last_updated");
        if (DateTimeOffset.TryParse(lastUpdated, out var parsedLastUpdated))
        {
            asOf = parsedLastUpdated;
        }

        return new MarketSnapshot
        {
            Ticker = holding.Ticker,
            Name = holding.Name,
            AssetType = holding.Type,
            ProviderSymbol = providerSymbol,
            Currency = "USD",
            CurrentPrice = currentPrice,
            DailyChangePercent = TryGetDecimal(market, "price_change_percentage_24h"),
            WeeklyChangePercent = TryGetDecimal(market, "price_change_percentage_7d_in_currency"),
            AsOf = asOf,
            IsAvailable = true,
        };
    }

    private string ResolveYahooSymbol(PortfolioHolding holding)
    {
        if (_tickerMappings.TryGetValue(holding.Ticker, out var mapping) && !string.IsNullOrWhiteSpace(mapping.YahooSymbol))
        {
            return mapping.YahooSymbol.Trim();
        }

        return holding.Ticker;
    }

    private string? ResolveCoinGeckoId(PortfolioHolding holding)
    {
        if (_tickerMappings.TryGetValue(holding.Ticker, out var mapping) && !string.IsNullOrWhiteSpace(mapping.CoinGeckoId))
        {
            return mapping.CoinGeckoId.Trim();
        }

        return DefaultCoinGeckoIds.TryGetValue(holding.Ticker, out var coinGeckoId)
            ? coinGeckoId
            : null;
    }

    private Uri BuildYahooChartUri(string symbol)
    {
        var baseUrl = _settings.YahooChartUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/{Uri.EscapeDataString(symbol)}?range=5d&interval=1d&includePrePost=false");
    }

    private Uri BuildCoinGeckoMarketsUri(string coinGeckoId)
    {
        var separator = _settings.CoinGeckoMarketsUrl.Contains('?') ? '&' : '?';
        var requestUri =
            $"{_settings.CoinGeckoMarketsUrl}{separator}vs_currency=usd&ids={Uri.EscapeDataString(coinGeckoId)}&price_change_percentage=7d";
        return new Uri(requestUri);
    }

    private static bool TryGetYahooResult(JsonElement root, out JsonElement result, out string errorMessage)
    {
        result = default;
        errorMessage = "Yahoo Finance returned an unexpected payload";

        if (!root.TryGetProperty("chart", out var chart))
        {
            return false;
        }

        if (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            errorMessage = TryGetString(error, "description") ?? errorMessage;
            return false;
        }

        if (!chart.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            errorMessage = "Yahoo Finance returned no quote data";
            return false;
        }

        result = results[0];
        return true;
    }

    private static decimal? TryGetYahooWeeklyChangePercent(JsonElement result)
    {
        if (!result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) ||
            quotes.ValueKind != JsonValueKind.Array ||
            quotes.GetArrayLength() == 0)
        {
            return null;
        }

        var quote = quotes[0];
        if (!quote.TryGetProperty("close", out var closes) || closes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<decimal>(closes.GetArrayLength());
        foreach (var close in closes.EnumerateArray())
        {
            var value = TryGetDecimal(close);
            if (value is not null)
            {
                values.Add(value.Value);
            }
        }

        if (values.Count < 2 || values[0] == 0)
        {
            return null;
        }

        return Math.Round(((values[^1] - values[0]) / values[0]) * 100m, 2);
    }

    private static bool IsCrypto(PortfolioHolding holding) =>
        string.Equals(holding.Type, "Crypto", StringComparison.OrdinalIgnoreCase);

    private static decimal? CalculatePercentChange(decimal? currentPrice, decimal? referencePrice)
    {
        if (currentPrice is null || referencePrice is null || referencePrice == 0)
        {
            return null;
        }

        return Math.Round(((currentPrice.Value - referencePrice.Value) / referencePrice.Value) * 100m, 2);
    }

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? TryGetDecimal(property)
            : null;
    }

    private static decimal? TryGetDecimal(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var doubleValue))
        {
            return (decimal)doubleValue;
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
    }

    private static DateTimeOffset? TryGetUnixTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (!property.TryGetInt64(out var unixTimeSeconds))
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
    }

    private static MarketSnapshot CreateUnavailableSnapshot(
        PortfolioHolding holding,
        string providerSymbol,
        string note)
    {
        return new MarketSnapshot
        {
            Ticker = holding.Ticker,
            Name = holding.Name,
            AssetType = holding.Type,
            ProviderSymbol = providerSymbol,
            IsAvailable = false,
            Note = note,
        };
    }
}