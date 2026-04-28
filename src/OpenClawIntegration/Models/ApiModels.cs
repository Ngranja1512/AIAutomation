using System.Text.Json.Serialization;

namespace OpenClawIntegration.Models;

// ── GitHub Copilot chat-completions API ──────────────────────────────────────

public class CopilotChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o";

    [JsonPropertyName("messages")]
    public List<CopilotMessage> Messages { get; set; } = [];

    [JsonPropertyName("max_tokens")]
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

// ── OpenClaw agent API ───────────────────────────────────────────────────────

public class OpenClawTaskRequest
{
    [JsonPropertyName("topic")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Copilot model forwarded to OpenClaw so it can use the caller's
    /// GitHub Copilot account as the underlying LLM.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "github-copilot";

    [JsonPropertyName("copilot_token")]
    public string CopilotToken { get; set; } = string.Empty;
}

public class OpenClawTaskResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
