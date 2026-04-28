namespace OpenClawIntegration.Models;

public class SummaryResult
{
    public string TopicName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
