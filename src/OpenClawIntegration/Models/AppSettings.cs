namespace OpenClawIntegration.Models;

public class AppSettings
{
    public CopilotSettings Copilot { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public SchedulerSettings Scheduler { get; set; } = new();
    public List<Topic> Topics { get; set; } = [];
}

public class CopilotSettings
{
    /// <summary>GitHub personal access token with <c>models: read</c> permission.</summary>
    public string Token { get; set; } = string.Empty;

    public string ApiUrl { get; set; } = "https://models.inference.ai.azure.com";

    /// <summary>Copilot model to use for summarisation.</summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>Maximum tokens in each summary response.</summary>
    public int MaxTokens { get; set; } = 1000;
}

public class EmailSettings
{
    /// <summary>SMTP server hostname, e.g. "smtp.gmail.com".</summary>
    public string SmtpHost { get; set; } = string.Empty;

    /// <summary>SMTP server port, e.g. 587 (STARTTLS) or 465 (SSL).</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>SMTP username / login address.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP password or app-specific password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Sender e-mail address shown in the From header.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Recipient e-mail address.</summary>
    public string ToAddress { get; set; } = string.Empty;
}

public class SchedulerSettings
{
    /// <summary>Cron expression (5-part, UTC). Default: every day at 08:00 UTC.</summary>
    public string CronExpression { get; set; } = "0 8 * * *";
}
