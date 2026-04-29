using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawIntegration.Models;
using OpenClawIntegration.Services;
using OpenClawIntegration.Workers;
using Moq;

namespace OpenClawIntegration.Tests.Services;

public class SummaryWorkerTests
{
    private static AppSettings BuildSettings(IEnumerable<Topic>? topics = null) =>
        new()
        {
            Scheduler = new SchedulerSettings { CronExpression = "0 8 * * *" },
            Topics = (topics ?? []).ToList(),
        };

    [Fact]
    public async Task RunAsync_ResearchesAllTopics_AndSendsSummaries()
    {
        // Arrange
        var topics = new[]
        {
            new Topic { Name = "AI News" },
            new Topic { Name = "Tech Trends" },
        };

        var results = topics
            .Select(t => new SummaryResult { TopicName = t.Name, IsSuccess = true, Summary = $"{t.Name} summary." })
            .ToList();

        var researchMock = new Mock<IResearchService>();
        researchMock
            .Setup(s => s.ResearchTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<Topic, CancellationToken, IResearchService, SummaryResult>(
                (topic, _) => results.First(r => r.TopicName == topic.Name));

        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(s => s.SendSummariesAsync(It.IsAny<IReadOnlyList<SummaryResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(BuildSettings(topics));
        var worker = new SummaryWorker(
            researchMock.Object,
            emailMock.Object,
            options,
            NullLogger<SummaryWorker>.Instance);

        // Act
        await worker.RunAsync();

        // Assert – each topic was researched exactly once
        researchMock.Verify(
            s => s.ResearchTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()),
            Times.Exactly(topics.Length));

        // Assert – summaries were sent exactly once
        emailMock.Verify(
            s => s.SendSummariesAsync(It.IsAny<IReadOnlyList<SummaryResult>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_DoesNotSendMessages_WhenNoTopicsConfigured()
    {
        // Arrange
        var researchMock = new Mock<IResearchService>();
        var emailMock = new Mock<IEmailService>();

        var options = Options.Create(BuildSettings(topics: []));
        var worker = new SummaryWorker(
            researchMock.Object,
            emailMock.Object,
            options,
            NullLogger<SummaryWorker>.Instance);

        // Act
        await worker.RunAsync();

        // Assert
        researchMock.Verify(
            s => s.ResearchTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()),
            Times.Never);
        emailMock.Verify(
            s => s.SendSummariesAsync(It.IsAny<IReadOnlyList<SummaryResult>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_ContinuesResearch_WhenOneTopicFails()
    {
        // Arrange
        var topics = new[]
        {
            new Topic { Name = "Topic A" },
            new Topic { Name = "Topic B" },
        };

        var researchMock = new Mock<IResearchService>();
        researchMock
            .Setup(s => s.ResearchTopicAsync(
                It.Is<Topic>(t => t.Name == "Topic A"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummaryResult { TopicName = "Topic A", IsSuccess = false, ErrorMessage = "Error" });

        researchMock
            .Setup(s => s.ResearchTopicAsync(
                It.Is<Topic>(t => t.Name == "Topic B"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SummaryResult { TopicName = "Topic B", IsSuccess = true, Summary = "Summary B" });

        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(s => s.SendSummariesAsync(It.IsAny<IReadOnlyList<SummaryResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(BuildSettings(topics));
        var worker = new SummaryWorker(
            researchMock.Object,
            emailMock.Object,
            options,
            NullLogger<SummaryWorker>.Instance);

        // Act – should not throw even if one topic failed
        await worker.RunAsync();

        // Assert – all topics attempted and summaries (including the failed one) sent
        researchMock.Verify(
            s => s.ResearchTopicAsync(It.IsAny<Topic>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        emailMock.Verify(
            s => s.SendSummariesAsync(
                It.Is<IReadOnlyList<SummaryResult>>(r => r.Count == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
