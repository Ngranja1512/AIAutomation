using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Retrieves insider trades from SEC EDGAR Form 4 filings.
/// Uses three free, unauthenticated EDGAR endpoints:
///   1. /cgi-bin/browse-edgar  – resolves ticker → CIK (Atom feed)
///   2. data.sec.gov/submissions – lists recent Form 4 accession numbers
///   3. /Archives/edgar/data   – downloads individual Form 4 XML documents
/// </summary>
public sealed partial class SecEdgarInsiderDataService : IInsiderDataService
{
    private const string BrowseBaseUrl      = "https://www.sec.gov/cgi-bin/browse-edgar";
    private const string SubmissionsBaseUrl = "https://data.sec.gov/submissions";
    private const string ArchiveBaseUrl     = "https://www.sec.gov/Archives/edgar/data";

    private readonly HttpClient                          _http;
    private readonly InsiderDataSettings                 _settings;
    private readonly ILogger<SecEdgarInsiderDataService> _logger;

    public SecEdgarInsiderDataService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<SecEdgarInsiderDataService> logger)
    {
        _http     = http;
        _settings = options.Value.InsiderData;
        _logger   = logger;

        // SEC EDGAR requires a descriptive User-Agent containing a contact address.
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"CopilotDigest/1.0 ({_settings.ContactEmail})");
        }
    }

    public async Task<IReadOnlyList<InsiderTrade>> GetRecentTradesAsync(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);

        if (!_settings.Enabled)
        {
            return [];
        }

        try
        {
            var cik = await ResolveCikAsync(ticker, cancellationToken);
            if (cik is null)
            {
                _logger.LogDebug("EDGAR: no CIK found for {Ticker}", ticker);
                return [];
            }

            var filings = await GetRecentForm4FilingsAsync(cik, cancellationToken);
            if (filings.Count == 0)
            {
                return [];
            }

            var trades = new List<InsiderTrade>();
            foreach (var filing in filings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fileTrades = await ParseForm4XmlAsync(cik, filing, cancellationToken);
                    trades.AddRange(fileTrades);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "EDGAR: failed to parse Form 4 {Accession} for {Ticker}",
                        filing.AccessionNumber, ticker);
                }
            }

            return trades;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EDGAR: failed to fetch insider data for {Ticker}", ticker);
            return [];
        }
    }

    // ── Step 1: Resolve ticker → CIK ────────────────────────────────────────

    private async Task<string?> ResolveCikAsync(string ticker, CancellationToken ct)
    {
        var url = $"{BrowseBaseUrl}?action=getcompany" +
                  $"&CIK={Uri.EscapeDataString(ticker)}" +
                  "&type=4&dateb=&owner=include&count=1&output=atom";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var match = CikInUrlPattern().Match(body);
        return match.Success ? match.Groups[1].Value : null;
    }

    // ── Step 2: List recent Form 4 accession numbers ─────────────────────────

    private async Task<IReadOnlyList<Form4Filing>> GetRecentForm4FilingsAsync(
        string cik, CancellationToken ct)
    {
        var paddedCik = cik.PadLeft(10, '0');
        var url = $"{SubmissionsBaseUrl}/CIK{paddedCik}.json";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("filings", out var filings) ||
            !filings.TryGetProperty("recent", out var recent))
        {
            return [];
        }

        if (!recent.TryGetProperty("form",           out var forms)      ||
            !recent.TryGetProperty("accessionNumber", out var accessions) ||
            !recent.TryGetProperty("primaryDocument", out var primaryDocs))
        {
            return [];
        }

        var formArr       = forms.EnumerateArray().ToArray();
        var accessionArr  = accessions.EnumerateArray().ToArray();
        var primaryDocArr = primaryDocs.EnumerateArray().ToArray();

        var result = new List<Form4Filing>();

        for (var i = 0; i < formArr.Length && result.Count < _settings.MaxFilings; i++)
        {
            if (formArr[i].GetString() != "4")
            {
                continue;
            }

            var accession  = accessionArr[i].GetString()  ?? string.Empty;
            var primaryDoc = primaryDocArr[i].GetString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(primaryDoc))
            {
                result.Add(new Form4Filing(accession, primaryDoc));
            }
        }

        return result;
    }

    // ── Step 3: Download and parse Form 4 XML ────────────────────────────────

    private async Task<IReadOnlyList<InsiderTrade>> ParseForm4XmlAsync(
        string cik, Form4Filing filing, CancellationToken ct)
    {
        // Accession "0000320193-24-000001" → directory "000032019324000001"
        var dir = filing.AccessionNumber.Replace("-", "", StringComparison.Ordinal);
        var url = $"{ArchiveBaseUrl}/{cik}/{dir}/{filing.PrimaryDocument}";

        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var xml  = await response.Content.ReadAsStringAsync(ct);
        var root = XDocument.Parse(xml).Root;
        if (root is null)
        {
            return [];
        }

        var ownerName  = root.Descendants("rptOwnerName").FirstOrDefault()?.Value.Trim() ?? "Unknown";
        var ownerTitle = ResolveOwnerTitle(root);

        var trades = new List<InsiderTrade>();

        // Only parse non-derivative transactions (direct stock purchases / sales).
        // Derivative transactions (options/RSUs) are excluded to keep the signal clean.
        foreach (var txn in root.Descendants("nonDerivativeTransaction"))
        {
            var adCode = GetNestedValue(txn, "transactionAcquiredDisposedCode");
            if (string.IsNullOrWhiteSpace(adCode))
            {
                continue;
            }

            var sharesStr = GetNestedValue(txn, "transactionShares");
            var priceStr  = GetNestedValue(txn, "transactionPricePerShare");
            var dateStr   = GetNestedValue(txn, "transactionDate");

            decimal? shares = decimal.TryParse(sharesStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : null;
            decimal? price  = decimal.TryParse(priceStr,  NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : null;
            DateOnly? date  = DateOnly.TryParse(dateStr, out var d) ? d : null;

            trades.Add(new InsiderTrade
            {
                InsiderName        = ownerName,
                InsiderTitle       = ownerTitle,
                AcquiredOrDisposed = adCode,
                Shares             = shares,
                PricePerShare      = price,
                TransactionDate    = date,
            });
        }

        return trades;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ResolveOwnerTitle(XElement root)
    {
        var title = root.Descendants("officerTitle").FirstOrDefault()?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        var isDirector = root.Descendants("isDirector").FirstOrDefault()?.Value == "1";
        var isTenPct   = root.Descendants("isTenPercentOwner").FirstOrDefault()?.Value == "1";

        return (isDirector, isTenPct) switch
        {
            (true, _) => "Director",
            (_, true) => "10% Owner",
            _         => "Insider",
        };
    }

    /// <summary>
    /// Reads the first child &lt;value&gt; element nested inside the named element.
    /// Form 4 XML wraps every field in a typed container, e.g.:
    /// &lt;transactionShares&gt;&lt;value&gt;1000&lt;/value&gt;&lt;/transactionShares&gt;
    /// </summary>
    private static string? GetNestedValue(XElement parent, string elementName) =>
        parent.Descendants(elementName)
              .FirstOrDefault()
              ?.Descendants("value")
              .FirstOrDefault()
              ?.Value
              .Trim();

    /// <summary>Matches /edgar/data/{cik}/ in EDGAR Atom feed responses.</summary>
    [GeneratedRegex(@"/edgar/data/(\d+)/")]
    private static partial Regex CikInUrlPattern();

    private sealed record Form4Filing(string AccessionNumber, string PrimaryDocument);
}
