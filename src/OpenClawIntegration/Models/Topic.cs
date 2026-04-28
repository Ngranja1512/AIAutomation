namespace OpenClawIntegration.Models;

public class Topic
{
    /// <summary>Short display name for the topic, e.g. "AI News".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional longer description used to provide context to the AI.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional custom prompt override.  When null, a default prompt is
    /// constructed from <see cref="Name"/> and <see cref="Description"/>.
    /// </summary>
    public string? Prompt { get; set; }
}
