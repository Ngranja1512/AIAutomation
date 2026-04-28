using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>
/// Calls the GitHub Copilot chat-completions endpoint
/// (<c>https://api.githubcopilot.com/chat/completions</c>) to generate
/// an AI-powered summary for a given topic.
/// </summary>
public class CopilotService : ICopilotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly CopilotSettings _settings;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(
        HttpClient http,
        IOptions<AppSettings> options,
        ILogger<CopilotService> logger)
    {
        _http = http;
        _settings = options.Value.Copilot;
        _logger = logger;

        _http.BaseAddress = new Uri(_settings.ApiUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.Token);
        _http.DefaultRequestHeaders.Add("Copilot-Integration-Id", "openclaw-integration");
    }

    public async Task<string> SummariseTopicAsync(Topic topic, CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(topic);
        var request = new CopilotChatRequest
        {
            Model = _settings.Model,
            MaxTokens = _settings.MaxTokens,
            Messages =
            [
                new CopilotMessage
                {
                    Role = "system",
                    Content = "You are a research assistant. Provide concise, " +
                              "informative summaries of the latest information on the given topic. " +
                              "Structure your response with bullet points for key takeaways.",
                },
                new CopilotMessage { Role = "user", Content = prompt },
            ],
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Requesting Copilot summary for topic: {Topic}", topic.Name);

        using var response = await _http.PostAsync("chat/completions", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<CopilotChatResponse>(responseJson, JsonOptions);

        var summary = chatResponse?.Choices.FirstOrDefault()?.Message.Content
                      ?? "No summary was returned by the Copilot API.";

        _logger.LogInformation("Received Copilot summary for topic: {Topic}", topic.Name);
        return summary;
    }

    private static string BuildPrompt(Topic topic)
    {
        if (!string.IsNullOrWhiteSpace(topic.Prompt))
            return topic.Prompt;

        var sb = new StringBuilder();
        sb.Append($"Research and summarise the latest information about: {topic.Name}.");
        if (!string.IsNullOrWhiteSpace(topic.Description))
            sb.Append($" Context: {topic.Description}");
        sb.Append(" Focus on the most important developments from the past 24 hours.");
        return sb.ToString();
    }
}
