using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Fetches fundamental financial data from Yahoo Finance.
///
/// Primary path  — v10 quoteSummary (full fundamentals: margins, FCF, revenue history, etc.)
///   Yahoo requires a crumb token since 2024. We acquire it with two unauthenticated calls:
///     1. GET fc.yahoo.com  → receives a session cookie
///     2. GET /v1/test/getcrumb (with cookie) → plain-text crumb string
///   The crumb is cached per process; on a 401/429 we refresh it once and retry.
///
/// Fallback path — v7/finance/quote (no crumb needed)
///   Provides market cap, trailing/forward PE, EPS, earnings timestamp, and analyst rating.
///   Used when quoteSummary is unavailable so the model still gets live valuation data.
/// </summary>
public sealed class YahooFinanceQuoteSummaryService : IFinancialDataService
{
    private readonly HttpClient            _http;
    private readonly FinancialDataSettings _settings;
    private readonly ILogger<YahooFinanceQuoteSummaryService> _logger;

    // Crumb state — refreshed lazily and on 401.
    private string? _crumb;
    private readonly SemaphoreSlim _crumbLock = new(1, 1);

    public YahooFinanceQuoteSummaryService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<YahooFinanceQuoteSummaryService> logger)
    {
        _http     = http;
        _settings = options.Value.FinancialData;
        _logger   = logger;

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CopilotDigest/1.0");
        }

        // Accept cookies — required to carry the Yahoo session cookie from fc.yahoo.com
        // to the crumb endpoint.  The IHttpClientFactory-managed handler needs this flag;
        // if the handler was already built without it the cookie jar will simply be empty
        // and the crumb request may still succeed on some regions (fails gracefully to fallback).
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<FinancialSnapshot?> GetFinancialSnapshotAsync(
        string yahooSymbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooSymbol);

        if (!_settings.Enabled)
        {
            return null;
        }

        // Try the full quoteSummary path first.
        var snapshot = await TryQuoteSummaryAsync(yahooSymbol, cancellationToken);
        if (snapshot is not null)
        {
            return snapshot;
        }

        // Fall back to the lighter v7/quote endpoint (no crumb, market cap + valuation only).
        _logger.LogWarning(
            "quoteSummary failed for {Symbol} — falling back to v7/finance/quote (partial data)",
            yahooSymbol);

        return await TryQuoteV7Async(yahooSymbol, cancellationToken);
    }

    // ── quoteSummary path ────────────────────────────────────────────────────

    private async Task<FinancialSnapshot?> TryQuoteSummaryAsync(
        string yahooSymbol, CancellationToken ct)
    {
        try
        {
            var crumb = await EnsureCrumbAsync(ct);
            var snapshot = await FetchQuoteSummaryAsync(yahooSymbol, crumb, ct);
            if (snapshot is not null)
            {
                return snapshot;
            }

            // Could be a stale crumb — refresh once and retry.
            _logger.LogDebug("quoteSummary returned no data for {Symbol}, refreshing crumb", yahooSymbol);
            crumb = await RefreshCrumbAsync(ct);
            return await FetchQuoteSummaryAsync(yahooSymbol, crumb, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "quoteSummary path failed for {Symbol}", yahooSymbol);
            return null;
        }
    }

    private async Task<FinancialSnapshot?> FetchQuoteSummaryAsync(
        string yahooSymbol, string? crumb, CancellationToken ct)
    {
        var baseUrl = _settings.QuoteSummaryUrl.TrimEnd('/');
        var url = $"{baseUrl}/{Uri.EscapeDataString(yahooSymbol)}" +
                  "?modules=financialData%2CdefaultKeyStatistics%2CincomeStatementHistory%2CsummaryDetail%2CcalendarEvents" +
                  "&corsDomain=finance.yahoo.com";

        if (!string.IsNullOrWhiteSpace(crumb))
        {
            url += $"&crumb={Uri.EscapeDataString(crumb)}";
        }

        using var response = await _http.GetAsync(url, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _logger.LogError(
                "quoteSummary returned {Status} for {Symbol} — crumb token rejected",
                (int)response.StatusCode, yahooSymbol);
            // Reset crumb so next call forces a full refresh.
            _crumb = null;
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "quoteSummary returned {Status} for {Symbol}",
                (int)response.StatusCode, yahooSymbol);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("quoteSummary", out var qs) ||
            !qs.TryGetProperty("result", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var result = results[0];

        result.TryGetProperty("financialData",          out var fd);
        result.TryGetProperty("defaultKeyStatistics",   out var dks);
        result.TryGetProperty("incomeStatementHistory", out var ish);
        result.TryGetProperty("summaryDetail",          out var sd);
        result.TryGetProperty("calendarEvents",         out var ce);

        return new FinancialSnapshot
        {
            Ticker           = yahooSymbol,
            MarketCap        = TryGetRaw(sd,  "marketCap") ?? TryGetRaw(dks, "marketCap"),
            TrailingPE       = TryGetRaw(sd,  "trailingPE") ?? TryGetRaw(dks, "trailingPE"),
            ForwardPE        = TryGetRaw(sd,  "forwardPE") ?? TryGetRaw(dks, "forwardPE"),
            TrailingEps      = TryGetRaw(dks, "trailingEps"),
            ForwardEps       = TryGetRaw(dks, "forwardEps"),
            RevenueTTM       = TryGetRaw(fd,  "totalRevenue"),
            RevenueGrowthYoY = TryGetRaw(fd,  "revenueGrowth"),
            GrossMargins     = TryGetRaw(fd,  "grossMargins"),
            OperatingMargins = TryGetRaw(fd,  "operatingMargins"),
            ProfitMargins    = TryGetRaw(fd,  "profitMargins"),
            FreeCashflow     = TryGetRaw(fd,  "freeCashflow"),
            TotalCash        = TryGetRaw(fd,  "totalCash"),
            TotalDebt        = TryGetRaw(fd,  "totalDebt"),
            AnalystConsensus = TryGetString(fd, "recommendationKey"),
            AnalystMean      = TryGetRaw(fd,  "recommendationMean"),
            NextEarningsDate = ParseNextEarningsDate(ce),
            AnnualHistory    = ParseAnnualHistory(ish),
        };
    }

    // ── v7/quote fallback ────────────────────────────────────────────────────

    private async Task<FinancialSnapshot?> TryQuoteV7Async(string yahooSymbol, CancellationToken ct)
    {
        try
        {
            var url = $"{_settings.QuoteV7Url.TrimEnd('/')}?symbols={Uri.EscapeDataString(yahooSymbol)}&corsDomain=finance.yahoo.com";

            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "v7/quote returned {Status} for {Symbol}",
                    (int)response.StatusCode, yahooSymbol);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            // Response: { "quoteResponse": { "result": [ {...} ] } }
            if (!doc.RootElement.TryGetProperty("quoteResponse", out var qr) ||
                !qr.TryGetProperty("result", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var q = results[0];

            // earningsTimestamp is a plain Unix integer (not wrapped).
            DateOnly? nextEarnings = null;
            if (q.TryGetProperty("earningsTimestamp", out var et) &&
                et.ValueKind == JsonValueKind.Number &&
                et.TryGetInt64(out var ts) && ts > 0)
            {
                nextEarnings = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime);
            }

            // averageAnalystRating is a string like "1.5 - Buy"
            string? analystConsensus = null;
            decimal? analystMean = null;
            if (q.TryGetProperty("averageAnalystRating", out var aar) &&
                aar.ValueKind == JsonValueKind.String)
            {
                var raw = aar.GetString() ?? string.Empty;
                // Format: "1.5 - Buy"  →  split on " - "
                var parts = raw.Split(" - ", 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    analystConsensus = parts[1].ToLowerInvariant();
                    if (decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var mean))
                    {
                        analystMean = mean;
                    }
                }
                else
                {
                    analystConsensus = raw.ToLowerInvariant();
                }
            }

            return new FinancialSnapshot
            {
                Ticker           = yahooSymbol,
                MarketCap        = TryGetDecimalDirect(q, "marketCap"),
                TrailingPE       = TryGetDecimalDirect(q, "trailingPE"),
                ForwardPE        = TryGetDecimalDirect(q, "forwardPE"),
                TrailingEps      = TryGetDecimalDirect(q, "epsTrailingTwelveMonths"),
                ForwardEps       = TryGetDecimalDirect(q, "epsForward"),
                AnalystConsensus = analystConsensus,
                AnalystMean      = analystMean,
                NextEarningsDate = nextEarnings,
                // Revenue / margins / FCF not available in v7; left null.
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "v7/quote fallback failed for {Symbol}", yahooSymbol);
            return null;
        }
    }

    // ── Crumb management ─────────────────────────────────────────────────────

    private async Task<string?> EnsureCrumbAsync(CancellationToken ct)
    {
        if (_crumb is not null)
        {
            return _crumb;
        }

        return await RefreshCrumbAsync(ct);
    }

    private async Task<string?> RefreshCrumbAsync(CancellationToken ct)
    {
        await _crumbLock.WaitAsync(ct);
        try
        {
            // Double-check: another thread may have refreshed while we waited.
            if (_crumb is not null)
            {
                return _crumb;
            }

            // Step 1: seed request to fc.yahoo.com to receive the session cookie.
            try
            {
                using var seedResp = await _http.GetAsync(_settings.CrumbSeedUrl, ct);
                _logger.LogDebug("Yahoo crumb seed returned {Status}", (int)seedResp.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Yahoo crumb seed request failed (non-fatal)");
            }

            // Step 2: fetch the crumb token.
            using var crumbResp = await _http.GetAsync(_settings.CrumbUrl, ct);
            if (!crumbResp.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Yahoo crumb endpoint returned {Status} — quoteSummary will likely fail",
                    (int)crumbResp.StatusCode);
                return null;
            }

            var crumb = (await crumbResp.Content.ReadAsStringAsync(ct)).Trim();
            if (string.IsNullOrEmpty(crumb))
            {
                _logger.LogError("Yahoo crumb endpoint returned an empty crumb string");
                return null;
            }

            _logger.LogDebug("Yahoo crumb refreshed successfully");
            _crumb = crumb;
            return _crumb;
        }
        finally
        {
            _crumbLock.Release();
        }
    }

    // ── Parsing helpers ──────────────────────────────────────────────────────

    private static IReadOnlyList<AnnualFinancials> ParseAnnualHistory(JsonElement ish)
    {
        if (ish.ValueKind != JsonValueKind.Object ||
            !ish.TryGetProperty("incomeStatementHistory", out var stmts) ||
            stmts.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<AnnualFinancials>();

        foreach (var stmt in stmts.EnumerateArray())
        {
            DateTimeOffset? endDate = null;
            if (stmt.TryGetProperty("endDate", out var endDateEl) &&
                endDateEl.TryGetProperty("raw", out var rawTs) &&
                rawTs.TryGetInt64(out var ts))
            {
                endDate = DateTimeOffset.FromUnixTimeSeconds(ts);
            }

            if (endDate is null)
            {
                continue;
            }

            results.Add(new AnnualFinancials(
                endDate.Value.Year,
                TryGetRaw(stmt, "totalRevenue"),
                TryGetRaw(stmt, "netIncome")));
        }

        results.Sort((a, b) => a.Year.CompareTo(b.Year));
        return results;
    }

    /// <summary>
    /// Reads a Yahoo "raw/fmt" wrapped numeric field, or a plain JSON number.
    /// </summary>
    private static decimal? TryGetRaw(JsonElement parent, string fieldName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(fieldName, out var field))
        {
            return null;
        }

        if (field.ValueKind == JsonValueKind.Number)
        {
            return field.TryGetDecimal(out var d) ? d : null;
        }

        if (field.ValueKind == JsonValueKind.Object &&
            field.TryGetProperty("raw", out var raw) &&
            raw.ValueKind == JsonValueKind.Number)
        {
            return raw.TryGetDecimal(out var w) ? w : null;
        }

        return null;
    }

    /// <summary>
    /// Reads a plain (non-wrapped) JSON number — used for v7/quote fields.
    /// </summary>
    private static decimal? TryGetDecimalDirect(JsonElement parent, string fieldName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(fieldName, out var field) ||
            field.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return field.TryGetDecimal(out var d) ? d : null;
    }

    /// <summary>
    /// Reads a string field or a Yahoo "raw/fmt" wrapped string.
    /// </summary>
    private static string? TryGetString(JsonElement parent, string fieldName)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(fieldName, out var field))
        {
            return null;
        }

        if (field.ValueKind == JsonValueKind.String)
        {
            return field.GetString();
        }

        if (field.ValueKind == JsonValueKind.Object &&
            field.TryGetProperty("raw", out var raw) &&
            raw.ValueKind == JsonValueKind.String)
        {
            return raw.GetString();
        }

        return null;
    }

    /// <summary>
    /// Extracts the first upcoming earnings date from calendarEvents.
    /// Schema: { "earnings": { "earningsDate": [ { "raw": 1753660800 } ] } }
    /// </summary>
    private static DateOnly? ParseNextEarningsDate(JsonElement ce)
    {
        if (ce.ValueKind != JsonValueKind.Object ||
            !ce.TryGetProperty("earnings", out var earnings) ||
            !earnings.TryGetProperty("earningsDate", out var dates) ||
            dates.ValueKind != JsonValueKind.Array ||
            dates.GetArrayLength() == 0)
        {
            return null;
        }

        var first = dates[0];
        if (first.TryGetProperty("raw", out var raw) &&
            raw.ValueKind == JsonValueKind.Number &&
            raw.TryGetInt64(out var ts))
        {
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime);
        }

        return null;
    }
}
