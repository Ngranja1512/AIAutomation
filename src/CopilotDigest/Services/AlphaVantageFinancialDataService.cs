using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches fundamental financial data from Alpha Vantage — works from cloud/CI runners
/// unlike Yahoo Finance, which blocks AWS/Azure IP ranges.
///
/// Three API calls per symbol (free tier: 25 req/day):
///   1. OVERVIEW          → market cap, PE ratios, EPS, margins, analyst ratings
///   2. INCOME_STATEMENT  → annual revenue + net income history (last 4 years)
///   3. CASH_FLOW         → free cash flow, operating cash flow, capex, total cash
///
/// All Alpha Vantage fields are returned as strings; numeric "None" values are treated as null.
/// Requires a free API key from https://www.alphavantage.co/support/#api-key
/// </summary>
public sealed class AlphaVantageFinancialDataService : IFinancialDataService
{
    private readonly HttpClient           _http;
    private readonly AlphaVantageSettings _settings;
    private readonly FinancialDataSettings _financialSettings;
    private readonly ILogger<AlphaVantageFinancialDataService> _logger;

    public AlphaVantageFinancialDataService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<AlphaVantageFinancialDataService> logger)
    {
        _http              = http;
        _settings          = options.Value.AlphaVantage;
        _financialSettings = options.Value.FinancialData;
        _logger            = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CopilotDigest/1.0");
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<FinancialSnapshot?> GetFinancialSnapshotAsync(
        string yahooSymbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooSymbol);

        if (!_financialSettings.Enabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogWarning(
                "Alpha Vantage API key is not configured — financial data unavailable for {Symbol}. " +
                "Set CopilotDigest__AlphaVantage__ApiKey (or appsettings.json AlphaVantage.ApiKey) " +
                "to a free key from https://www.alphavantage.co/support/#api-key",
                yahooSymbol);
            return null;
        }

        // Alpha Vantage uses plain US ticker symbols; strip any Yahoo suffix (.L, -B, etc.)
        var symbol = NormaliseSymbol(yahooSymbol);

        try
        {
            // Run the three fetches concurrently — they are independent.
            var overviewTask         = FetchJsonAsync($"function=OVERVIEW&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
            var incomeStatementTask  = FetchJsonAsync($"function=INCOME_STATEMENT&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);
            var cashFlowTask         = FetchJsonAsync($"function=CASH_FLOW&symbol={Uri.EscapeDataString(symbol)}", cancellationToken);

            await Task.WhenAll(overviewTask, incomeStatementTask, cashFlowTask);

            using var overview        = await overviewTask;
            using var incomeStatement = await incomeStatementTask;
            using var cashFlow        = await cashFlowTask;

            if (overview is null)
            {
                _logger.LogError("Alpha Vantage OVERVIEW returned no data for {Symbol}", symbol);
                return null;
            }

            var root = overview.RootElement;

            // AV returns { "Information": "..." } when rate-limited or key is invalid.
            if (root.TryGetProperty("Information", out var info))
            {
                _logger.LogError(
                    "Alpha Vantage API returned an informational message for {Symbol}: {Message}",
                    symbol, info.GetString());
                return null;
            }

            // OVERVIEW fields
            var marketCap        = ParseAvDecimal(root, "MarketCapitalization");
            var trailingPE       = ParseAvDecimal(root, "PERatio");
            var forwardPE        = ParseAvDecimal(root, "ForwardPE");
            var trailingEps      = ParseAvDecimal(root, "EPS");
            var operatingMargins = ParseAvDecimal(root, "OperatingMarginTTM");
            var profitMargins    = ParseAvDecimal(root, "ProfitMargin");
            var revGrowth        = ParseAvDecimal(root, "QuarterlyRevenueGrowthYOY");
            var revenueTTM       = ParseAvDecimal(root, "RevenueTTM");
            var grossMargins     = ParseAvDecimal(root, "GrossProfitTTM") is { } gp && revenueTTM is { } rev && rev != 0
                                       ? gp / rev
                                       : (decimal?)null;

            // Analyst consensus from the 5 rating-count fields
            var (analystConsensus, analystMean) = ParseAnalystRatings(root);

            // INCOME_STATEMENT → annual history
            var annualHistory = ParseAnnualHistory(incomeStatement);

            // CASH_FLOW → FCF, total cash
            var (freeCashflow, totalCash) = ParseCashFlow(cashFlow);

            return new FinancialSnapshot
            {
                Ticker           = yahooSymbol,
                MarketCap        = marketCap,
                TrailingPE       = trailingPE,
                ForwardPE        = forwardPE,
                TrailingEps      = trailingEps,
                ForwardEps       = null,            // not available in AV OVERVIEW
                RevenueTTM       = revenueTTM,
                RevenueGrowthYoY = revGrowth,
                GrossMargins     = grossMargins,
                OperatingMargins = operatingMargins,
                ProfitMargins    = profitMargins,
                FreeCashflow     = freeCashflow,
                TotalCash        = totalCash,
                TotalDebt        = ParseAvDecimal(root, "BookValue"),  // not exact but best proxy available
                AnalystConsensus = analystConsensus,
                AnalystMean      = analystMean,
                NextEarningsDate = null,            // requires separate AV endpoint; omitted
                AnnualHistory    = annualHistory,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Alpha Vantage financial data for {Symbol}", symbol);
            return null;
        }
    }

    // ── HTTP helper ──────────────────────────────────────────────────────────

    private async Task<JsonDocument?> FetchJsonAsync(string queryString, CancellationToken ct)
    {
        var url = $"{_settings.BaseUrl.TrimEnd('/')}?{queryString}&apikey={Uri.EscapeDataString(_settings.ApiKey)}";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Alpha Vantage returned {Status} for query: {Query}", (int)response.StatusCode, queryString);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Alpha Vantage returns all values as JSON strings.
    /// "None" (and empty strings) are treated as null.
    /// </summary>
    private static decimal? ParseAvDecimal(JsonElement parent, string fieldName)
    {
        if (!parent.TryGetProperty(fieldName, out var field))
        {
            return null;
        }

        var raw = field.ValueKind == JsonValueKind.String
            ? field.GetString()
            : field.GetRawText();

        if (string.IsNullOrWhiteSpace(raw) || raw.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    private static string? ParseAvString(JsonElement parent, string fieldName)
    {
        if (!parent.TryGetProperty(fieldName, out var field) ||
            field.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = field.GetString();
        return string.IsNullOrWhiteSpace(s) || s.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? null
            : s;
    }

    /// <summary>
    /// Derives analyst consensus key and mean from Alpha Vantage's five rating-count fields.
    /// AV provides: AnalystRatingStrongBuy, AnalystRatingBuy, AnalystRatingHold, AnalystRatingSell, AnalystRatingStrongSell
    /// We compute weighted mean (Strong Buy=1, Buy=2, Hold=3, Sell=4, Strong Sell=5) then map:
    ///   &lt;1.75 → "strong-buy", 1.75–2.5 → "buy", 2.5–3.5 → "hold", 3.5–4.5 → "sell", &gt;4.5 → "strong-sell"
    /// </summary>
    private static (string? consensus, decimal? mean) ParseAnalystRatings(JsonElement root)
    {
        var sb  = ParseAvDecimal(root, "AnalystRatingStrongBuy")  ?? 0;
        var b   = ParseAvDecimal(root, "AnalystRatingBuy")        ?? 0;
        var h   = ParseAvDecimal(root, "AnalystRatingHold")       ?? 0;
        var s   = ParseAvDecimal(root, "AnalystRatingSell")       ?? 0;
        var ss  = ParseAvDecimal(root, "AnalystRatingStrongSell") ?? 0;

        var total = sb + b + h + s + ss;
        if (total == 0)
        {
            return (null, null);
        }

        var mean = (sb * 1 + b * 2 + h * 3 + s * 4 + ss * 5) / total;

        var key = mean switch
        {
            < 1.75m => "strong-buy",
            < 2.5m  => "buy",
            < 3.5m  => "hold",
            < 4.5m  => "sell",
            _       => "strong-sell",
        };

        return (key, mean);
    }

    /// <summary>
    /// Parses annual income statement reports into the shared AnnualFinancials list.
    /// Reports are newest-first in AV; we reverse to oldest-first.
    /// </summary>
    private static IReadOnlyList<AnnualFinancials> ParseAnnualHistory(JsonDocument? doc)
    {
        if (doc is null ||
            !doc.RootElement.TryGetProperty("annualReports", out var reports) ||
            reports.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<AnnualFinancials>();

        foreach (var report in reports.EnumerateArray().Take(4))
        {
            // fiscalDateEnding is a plain date string: "2024-12-31"
            var dateStr = ParseAvString(report, "fiscalDateEnding");
            if (dateStr is null ||
                !DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out var date))
            {
                continue;
            }

            var revenue   = ParseAvDecimal(report, "totalRevenue");
            var netIncome = ParseAvDecimal(report, "netIncome");

            results.Add(new AnnualFinancials(date.Year, revenue, netIncome));
        }

        // AV returns newest-first; reverse so the enricher/prompt reads oldest→newest trend.
        results.Sort((a, b) => a.Year.CompareTo(b.Year));
        return results;
    }

    /// <summary>
    /// Parses free cash flow and total cash from the CASH_FLOW endpoint.
    /// FCF = operatingCashflow − capitalExpenditures when the direct freeCashFlow field is absent.
    /// </summary>
    private static (decimal? freeCashflow, decimal? totalCash) ParseCashFlow(JsonDocument? doc)
    {
        if (doc is null ||
            !doc.RootElement.TryGetProperty("annualReports", out var reports) ||
            reports.ValueKind != JsonValueKind.Array ||
            reports.GetArrayLength() == 0)
        {
            return (null, null);
        }

        // Use most recent annual report (index 0).
        var latest = reports[0];

        var fcf = ParseAvDecimal(latest, "freeCashFlow");
        if (fcf is null)
        {
            var ocf  = ParseAvDecimal(latest, "operatingCashflow");
            var capex = ParseAvDecimal(latest, "capitalExpenditures");
            if (ocf.HasValue && capex.HasValue)
            {
                // AV stores capex as a positive number; FCF = OCF − capex.
                fcf = ocf.Value - Math.Abs(capex.Value);
            }
        }

        // AV CASH_FLOW doesn't have a total-cash field; leave null (OVERVIEW has no cash either).
        return (fcf, null);
    }

    /// <summary>
    /// Strips Yahoo Finance symbol suffixes (e.g. ".L", "-B", ".TO") not used by Alpha Vantage.
    /// US tickers are passed through unchanged.
    /// </summary>
    private static string NormaliseSymbol(string yahooSymbol)
    {
        var dot = yahooSymbol.IndexOf('.');
        return dot > 0 ? yahooSymbol[..dot] : yahooSymbol;
    }
}
