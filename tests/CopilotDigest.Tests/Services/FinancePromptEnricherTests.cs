using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using CopilotDigest.Models;
using CopilotDigest.Services;

namespace CopilotDigest.Tests.Services;

public class FinancePromptEnricherTests
{
    [Fact]
    public async Task EnrichTopicAsync_ReturnsOriginalTopic_WhenPromptHasNoPortfolioTable()
    {
        // Arrange
        var marketDataMock = new Mock<IFreeMarketDataService>(MockBehavior.Strict);
        var sut = new FinancePromptEnricher(marketDataMock.Object, NullLogger<FinancePromptEnricher>.Instance);
        var topic = new Topic { Name = "AI News", Prompt = "No holdings table here." };

        // Act
        var result = await sut.EnrichTopicAsync(topic);

        // Assert
        Assert.Same(topic, result);
    }

    [Fact]
    public async Task EnrichTopicAsync_PrependsSnapshot_WhenPortfolioTableIsPresent()
    {
        // Arrange
        IReadOnlyList<PortfolioHolding>? capturedHoldings = null;
        var marketDataMock = new Mock<IFreeMarketDataService>();
        marketDataMock
            .Setup(service => service.GetSnapshotsAsync(It.IsAny<IReadOnlyList<PortfolioHolding>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<PortfolioHolding>, CancellationToken>((holdings, _) => capturedHoldings = holdings)
            .ReturnsAsync([
                new MarketSnapshot
                {
                    Ticker = "MSF",
                    Name = "Microsoft",
                    AssetType = "Stock",
                    ProviderSymbol = "MSFT",
                    Currency = "USD",
                    CurrentPrice = 424.46m,
                    DailyChangePercent = -1.12m,
                    WeeklyChangePercent = 0.54m,
                    AsOf = new DateTimeOffset(2026, 4, 29, 19, 30, 0, TimeSpan.Zero),
                    IsAvailable = true,
                },
                new MarketSnapshot
                {
                    Ticker = "BTC",
                    Name = "Bitcoin",
                    AssetType = "Crypto",
                    ProviderSymbol = "bitcoin",
                    Currency = "USD",
                    CurrentPrice = 64000m,
                    DailyChangePercent = 2.1m,
                    WeeklyChangePercent = 6.5m,
                    AsOf = new DateTimeOffset(2026, 4, 29, 19, 31, 0, TimeSpan.Zero),
                    IsAvailable = true,
                },
            ]);

        var sut = new FinancePromptEnricher(marketDataMock.Object, NullLogger<FinancePromptEnricher>.Instance);

        var originalPrompt = """
## My Portfolio

| Ticker | Name | Type | Quantity | Avg Buy Price (€) |
|--------|------|------|----------|-------------------|
| MSF    | Microsoft | Stock | 1 | 340.85 |
| BTC    | Bitcoin | Crypto | 0.01276 | 32041 |

---

## Your Task
Analyse the portfolio.
""";

        var topic = new Topic { Name = "Portfolio Analysis", Prompt = originalPrompt };

        // Act
        var result = await sut.EnrichTopicAsync(topic);

        // Assert
        Assert.NotNull(capturedHoldings);
        Assert.Collection(
            capturedHoldings!,
            holding =>
            {
                Assert.Equal("MSF", holding.Ticker);
                Assert.Equal("Microsoft", holding.Name);
                Assert.Equal("Stock", holding.Type);
            },
            holding =>
            {
                Assert.Equal("BTC", holding.Ticker);
                Assert.Equal("Bitcoin", holding.Name);
                Assert.Equal("Crypto", holding.Type);
            });

        Assert.NotSame(topic, result);
        Assert.NotNull(result.Prompt);
        Assert.Contains("## Live Market Data Snapshot", result.Prompt);
        Assert.Contains("- MSF (MSFT): 424.46 USD, 1d -1.12%, 5d +0.54%, as of 2026-04-29 19:30 UTC", result.Prompt);
        Assert.Contains("- BTC (bitcoin): 64000 USD, 1d +2.1%, 7d +6.5%, as of 2026-04-29 19:31 UTC", result.Prompt);
        Assert.EndsWith(originalPrompt, result.Prompt, StringComparison.Ordinal);
    }
}