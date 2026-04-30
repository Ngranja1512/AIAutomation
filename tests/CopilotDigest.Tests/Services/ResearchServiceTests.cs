using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using CopilotDigest.Models;
using CopilotDigest.Services;

namespace CopilotDigest.Tests.Services;

public class ResearchServiceTests
{
    [Fact]
    public async Task ResearchTopicAsync_ReturnsSuccess_WhenCopilotSucceeds()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Latest AI news summary.");

        var sut = new ResearchService(copilotMock.Object, NullLogger<ResearchService>.Instance);

        var topic = new Topic { Name = "AI News", Description = "Artificial intelligence." };

        // Act
        var result = await sut.ResearchTopicAsync(topic);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("AI News", result.TopicName);
        Assert.Equal("Latest AI news summary.", result.Summary);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ResearchTopicAsync_ReturnsFailure_WhenCopilotThrows()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Unauthorized"));

        var sut = new ResearchService(copilotMock.Object, NullLogger<ResearchService>.Instance);

        var topic = new Topic { Name = "Failing Topic" };

        // Act
        var result = await sut.ResearchTopicAsync(topic);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Failing Topic", result.TopicName);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Unauthorized", result.ErrorMessage);
    }

    [Fact]
    public async Task ResearchTopicAsync_DelegatesToCopilotService()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary via Copilot.");

        var sut = new ResearchService(copilotMock.Object, NullLogger<ResearchService>.Instance);

        var topic = new Topic { Name = "Test" };

        // Act
        var result = await sut.ResearchTopicAsync(topic);

        // Assert
        Assert.True(result.IsSuccess);
        copilotMock.Verify(
            s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResearchTopicAsync_EnrichesTopicBeforeDelegating()
    {
        // Arrange
        var forwardedTopic = default(Topic);
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .Callback<Topic, CancellationToken>((topic, _) => forwardedTopic = topic)
            .ReturnsAsync("Summary via Copilot.");

        var enricherMock = new Mock<IFinancePromptEnricher>();
        enricherMock
            .Setup(s => s.EnrichTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Topic topic, CancellationToken _) => new Topic
            {
                Name = topic.Name,
                Description = topic.Description,
                Prompt = "## Live Market Data Snapshot\n- MSFT: 424.46 USD",
            });

        var sut = new ResearchService(
            copilotMock.Object,
            enricherMock.Object,
            NullLogger<ResearchService>.Instance);

        var topic = new Topic { Name = "Portfolio Analysis", Prompt = "Original prompt" };

        // Act
        var result = await sut.ResearchTopicAsync(topic);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(forwardedTopic);
        Assert.Equal(topic.Name, forwardedTopic!.Name);
        Assert.Equal("## Live Market Data Snapshot\n- MSFT: 424.46 USD", forwardedTopic.Prompt);
        enricherMock.Verify(
            s => s.EnrichTopicAsync(topic, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResearchTopicAsync_SetsGeneratedAt_ToUtcNow()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary.");

        var before = DateTime.UtcNow;

        var sut = new ResearchService(copilotMock.Object, NullLogger<ResearchService>.Instance);

        var topic = new Topic { Name = "Time Test" };

        // Act
        var result = await sut.ResearchTopicAsync(topic);
        var after = DateTime.UtcNow;

        // Assert
        Assert.InRange(result.GeneratedAt, before, after);
    }
}
