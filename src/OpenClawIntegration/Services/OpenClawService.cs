using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>
/// Integrates with the OpenClaw AI agent platform (https://openclaw.ai/).
/// OpenClaw uses the caller's GitHub Copilot account as its underlying LLM.
/// When <see cref="OpenClawSettings.UseDirectCopilot"/> is <c>true</c>,
/// the Copilot API is called directly, bypassing OpenClaw.
/// </summary>
public class OpenClawService : IOpenClawService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly OpenClawSettings _openClawSettings;
    private readonly CopilotSettings _copilotSettings;
    private readonly ICopilotService _copilotService;
    private readonly ILogger<OpenClawService> _logger;

    public OpenClawService(
        HttpClient http,
        IOptions<AppSettings> options,
        ICopilotService copilotService,
        ILogger<OpenClawService> logger)
    {
        _http = http;
        _openClawSettings = options.Value.OpenClaw;
        _copilotSettings = options.Value.Copilot;
        _copilotService = copilotService;
        _logger = logger;

        _http.BaseAddress = new Uri(_openClawSettings.ApiUrl.TrimEnd('/') + "/");
    }

    public async Task<SummaryResult> ResearchTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        try
        {
            string summary;

            if (_openClawSettings.UseDirectCopilot)
            {
                _logger.LogInformation(
                    "Using GitHub Copilot directly for topic: {Topic}", topic.Name);
                summary = await _copilotService.SummariseTopicAsync(topic, cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "Using OpenClaw agent for topic: {Topic}", topic.Name);
                summary = await RunOpenClawAgentAsync(topic, cancellationToken);
            }

            return new SummaryResult
            {
                TopicName = topic.Name,
                Summary = summary,
                GeneratedAt = DateTime.UtcNow,
                IsSuccess = true,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to research topic: {Topic}", topic.Name);
            return new SummaryResult
            {
                TopicName = topic.Name,
                GeneratedAt = DateTime.UtcNow,
                IsSuccess = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    private async Task<string> RunOpenClawAgentAsync(Topic topic, CancellationToken cancellationToken)
    {
        var prompt = string.IsNullOrWhiteSpace(topic.Prompt)
            ? $"Research and summarise the latest information about: {topic.Name}. " +
              $"Context: {topic.Description}. Focus on the most important developments from the past 24 hours."
            : topic.Prompt;

        var taskRequest = new OpenClawTaskRequest
        {
            Topic = topic.Name,
            Description = topic.Description,
            Prompt = prompt,
            Model = "github-copilot",
            CopilotToken = _copilotSettings.Token,
        };

        var json = JsonSerializer.Serialize(taskRequest, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync("v1/tasks", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var taskResponse = JsonSerializer.Deserialize<OpenClawTaskResponse>(responseJson, JsonOptions);

        if (taskResponse is null || !string.IsNullOrWhiteSpace(taskResponse.Error))
            throw new InvalidOperationException(
                $"OpenClaw agent returned an error: {taskResponse?.Error ?? "unknown error"}");

        return taskResponse.Result;
    }
}
