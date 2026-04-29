using System.Text.Json.Serialization;

namespace CopilotDigest.Models;

// ── GitHub Copilot chat-completions API ──────────────────────────────────────

public class CopilotChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o";

    [JsonPropertyName("messages")]
    public List<CopilotMessage> Messages { get; set; } = [];

    [JsonPropertyName("max_completion_tokens")]
    public int MaxTokens { get; set; } = 1000;
}

public class CopilotMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class CopilotChatResponse
{
    [JsonPropertyName("choices")]
    public List<CopilotChoice> Choices { get; set; } = [];
}

public class CopilotChoice
{
    [JsonPropertyName("message")]
    public CopilotMessage Message { get; set; } = new();
}

public class CopilotModelsResponse
{
    [JsonPropertyName("data")]
    public List<CopilotModel> Data { get; set; } = [];
}

public class CopilotModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("task")]
    public string Task { get; set; } = string.Empty;
}
