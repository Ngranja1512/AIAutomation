using Microsoft.Extensions.Options;
using OpenClawIntegration.Models;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace OpenClawIntegration.Services;

/// <summary>
/// Sends topic summaries to a WhatsApp number via the Twilio API.
/// </summary>
public class WhatsAppService : IWhatsAppService
{
    private readonly WhatsAppSettings _settings;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        IOptions<AppSettings> options,
        ILogger<WhatsAppService> logger)
    {
        _settings = options.Value.WhatsApp;
        _logger = logger;
    }

    public async Task SendSummariesAsync(
        IReadOnlyList<SummaryResult> summaries,
        CancellationToken cancellationToken = default)
    {
        if (summaries.Count == 0)
        {
            _logger.LogInformation("No summaries to send.");
            return;
        }

        TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);

        var from = new PhoneNumber($"whatsapp:{_settings.FromNumber}");
        var to = new PhoneNumber($"whatsapp:{_settings.ToNumber}");

        var messageBody = FormatMessage(summaries);

        // WhatsApp messages are limited to 4096 characters per message.
        // Split into chunks if needed.
        var chunks = SplitIntoChunks(messageBody, maxLength: 4096);

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Sending WhatsApp message to {To}", _settings.ToNumber);

            await MessageResource.CreateAsync(
                body: chunk,
                from: from,
                to: to);

            _logger.LogInformation("WhatsApp message sent successfully.");
        }
    }

    private static string FormatMessage(IReadOnlyList<SummaryResult> summaries)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📊 *Daily Research Summary* – {date}");
        sb.AppendLine();

        foreach (var result in summaries)
        {
            if (result.IsSuccess)
            {
                sb.AppendLine($"🔍 *{result.TopicName}*");
                sb.AppendLine(result.Summary);
            }
            else
            {
                sb.AppendLine($"⚠️ *{result.TopicName}* – Failed: {result.ErrorMessage}");
            }

            sb.AppendLine();
            sb.AppendLine("───────────────────");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static List<string> SplitIntoChunks(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return [text];

        var chunks = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var length = Math.Min(maxLength, text.Length - offset);
            chunks.Add(text.Substring(offset, length));
            offset += length;
        }
        return chunks;
    }
}
