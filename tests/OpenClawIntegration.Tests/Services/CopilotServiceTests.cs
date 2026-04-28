using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using OpenClawIntegration.Models;
using OpenClawIntegration.Services;

namespace OpenClawIntegration.Tests.Services;

public class CopilotServiceTests
{
    private static AppSettings BuildSettings(string token = "test-token", string model = "gpt-4o") =>
        new()
        {
            Copilot = new CopilotSettings
            {
                Token = token,
                ApiUrl = "https://api.githubcopilot.com",
                Model = model,
                MaxTokens = 500,
            },
        };

    private static HttpClient BuildMockHttpClient(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(handlerMock.Object);
    }

    [Fact]
    public async Task SummariseTopicAsync_ReturnsSummary_WhenApiSucceeds()
    {
        // Arrange
        var apiResponse = new CopilotChatResponse
        {
            Choices =
            [
                new CopilotChoice
                {
                    Message = new CopilotMessage { Role = "assistant", Content = "AI is evolving fast." },
                },
            ],
        };

        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        var http = BuildMockHttpClient(httpResponse);
        var options = Options.Create(BuildSettings());
        var sut = new CopilotService(http, options, NullLogger<CopilotService>.Instance);

        var topic = new Topic { Name = "AI News", Description = "Latest AI developments" };

        // Act
        var result = await sut.SummariseTopicAsync(topic);

        // Assert
        Assert.Equal("AI is evolving fast.", result);
    }

    [Fact]
    public async Task SummariseTopicAsync_UsesCustomPrompt_WhenTopicPromptIsSet()
    {
        // Arrange
        string? capturedBody = null;

        var apiResponse = new CopilotChatResponse
        {
            Choices =
            [
                new CopilotChoice
                {
                    Message = new CopilotMessage { Role = "assistant", Content = "Custom result." },
                },
            ],
        };

        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                // Read the body synchronously during the callback, before the using block disposes it.
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });

        var http = new HttpClient(handlerMock.Object);
        var options = Options.Create(BuildSettings());
        var sut = new CopilotService(http, options, NullLogger<CopilotService>.Instance);

        var topic = new Topic
        {
            Name = "Crypto",
            Prompt = "Tell me about Bitcoin prices today.",
        };

        // Act
        await sut.SummariseTopicAsync(topic);

        // Assert – the custom prompt should appear in the request body
        Assert.NotNull(capturedBody);
        Assert.Contains("Tell me about Bitcoin prices today.", capturedBody);
    }

    [Fact]
    public async Task SummariseTopicAsync_ReturnsDefault_WhenNoChoicesReturned()
    {
        // Arrange
        var apiResponse = new CopilotChatResponse { Choices = [] };
        var json = JsonSerializer.Serialize(apiResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var http = BuildMockHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

        var options = Options.Create(BuildSettings());
        var sut = new CopilotService(http, options, NullLogger<CopilotService>.Instance);
        var topic = new Topic { Name = "Test Topic" };

        // Act
        var result = await sut.SummariseTopicAsync(topic);

        // Assert
        Assert.Equal("No summary was returned by the Copilot API.", result);
    }

    [Fact]
    public async Task SummariseTopicAsync_Throws_WhenApiReturnsError()
    {
        // Arrange
        var http = BuildMockHttpClient(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var options = Options.Create(BuildSettings());
        var sut = new CopilotService(http, options, NullLogger<CopilotService>.Instance);
        var topic = new Topic { Name = "Test Topic" };

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => sut.SummariseTopicAsync(topic));
    }
}
