using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenClawIntegration.Models;
using OpenClawIntegration.Services;

namespace OpenClawIntegration.Tests.Services;

public class OpenClawServiceTests
{
    private static AppSettings BuildSettings(bool useDirectCopilot = true, string openClawKey = "") =>
        new()
        {
            Copilot = new CopilotSettings
            {
                Token = "test-copilot-token",
                ApiUrl = "https://api.githubcopilot.com",
                Model = "gpt-4o",
            },
            OpenClaw = new OpenClawSettings
            {
                ApiKey = openClawKey,
                ApiUrl = "https://api.openclaw.ai",
                UseDirectCopilot = useDirectCopilot,
            },
        };

    [Fact]
    public async Task ResearchTopicAsync_ReturnsSuccess_WhenCopilotSucceeds()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Latest AI news summary.");

        var http = new System.Net.Http.HttpClient();
        var options = Options.Create(BuildSettings(useDirectCopilot: true));
        var sut = new OpenClawService(
            http, options, copilotMock.Object, NullLogger<OpenClawService>.Instance);

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

        var http = new System.Net.Http.HttpClient();
        var options = Options.Create(BuildSettings(useDirectCopilot: true));
        var sut = new OpenClawService(
            http, options, copilotMock.Object, NullLogger<OpenClawService>.Instance);

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
    public async Task ResearchTopicAsync_UsesCopilotDirectly_WhenApiKeyIsEmpty()
    {
        // Arrange
        var copilotMock = new Mock<ICopilotService>();
        copilotMock
            .Setup(s => s.SummariseTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary via Copilot.");

        var http = new System.Net.Http.HttpClient();
        // ApiKey is empty → should fall back to direct Copilot even if UseDirectCopilot is false
        var options = Options.Create(BuildSettings(useDirectCopilot: false, openClawKey: ""));
        var sut = new OpenClawService(
            http, options, copilotMock.Object, NullLogger<OpenClawService>.Instance);

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

        var http = new System.Net.Http.HttpClient();
        var options = Options.Create(BuildSettings(useDirectCopilot: true));
        var sut = new OpenClawService(
            http, options, copilotMock.Object, NullLogger<OpenClawService>.Instance);

        var topic = new Topic { Name = "Time Test" };

        // Act
        var result = await sut.ResearchTopicAsync(topic);
        var after = DateTime.UtcNow;

        // Assert
        Assert.InRange(result.GeneratedAt, before, after);
    }
}
