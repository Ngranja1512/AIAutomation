using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>
/// Sends topic summaries to a configured e-mail address via SMTP.
/// </summary>
public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<AppSettings> options,
        ILogger<EmailService> logger)
    {
        _settings = options.Value.Email;
        _logger = logger;
    }

    public async Task SendSummariesAsync(
        IReadOnlyList<SummaryResult> summaries,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            _logger.LogError("Email FromAddress is not configured. Set the EMAIL_FROM_ADDRESS secret/environment variable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.ToAddress))
        {
            _logger.LogError("Email ToAddress is not configured. Set the EMAIL_TO_ADDRESS secret/environment variable.");
            return;
        }

        if (summaries.Count == 0)
        {
            _logger.LogInformation("No summaries to send.");
            return;
        }

        var subject = $"Daily Research Summary – {DateTime.UtcNow:yyyy-MM-dd HH:mm UTC}";
        var body = FormatMessage(summaries);

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_settings.Username, _settings.Password),
        };

        using var message = new MailMessage(_settings.FromAddress, _settings.ToAddress, subject, body);

        _logger.LogInformation("Sending email to {To}", _settings.ToAddress);

        await client.SendMailAsync(message, cancellationToken);

        _logger.LogInformation("Email sent successfully.");
    }

    private static string FormatMessage(IReadOnlyList<SummaryResult> summaries)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Daily Research Summary – {date}");
        sb.AppendLine();

        foreach (var result in summaries)
        {
            if (result.IsSuccess)
            {
                sb.AppendLine($"[{result.TopicName}]");
                sb.AppendLine(result.Summary);
            }
            else
            {
                sb.AppendLine($"[{result.TopicName}] – Failed: {result.ErrorMessage}");
            }

            sb.AppendLine();
            sb.AppendLine("-------------------");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
