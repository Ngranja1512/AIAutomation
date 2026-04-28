using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenClawIntegration.Models;
using OpenClawIntegration.Services;

namespace OpenClawIntegration.Tests.Services;

public class OpenClawServiceTests
{
    [Fact]
    public async Task ResearchTopicAsync_ReturnsSuccess_WhenCopilotSucceeds()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Latest AI news summary.");

        var sut = new OpenClawService(copilotMock.Object, NullLogger<OpenClawService>.Instance);

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

        var sut = new OpenClawService(copilotMock.Object, NullLogger<OpenClawService>.Instance);

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

        var sut = new OpenClawService(copilotMock.Object, NullLogger<OpenClawService>.Instance);

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
    public async Task ResearchTopicAsync_SetsGeneratedAt_ToUtcNow()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary.");

        var before = DateTime.UtcNow;

        var sut = new OpenClawService(copilotMock.Object, NullLogger<OpenClawService>.Instance);

        var topic = new Topic { Name = "Time Test" };

        // Act
        var result = await sut.ResearchTopicAsync(topic);
        var after = DateTime.UtcNow;

        // Assert
        Assert.InRange(result.GeneratedAt, before, after);
    }
}
