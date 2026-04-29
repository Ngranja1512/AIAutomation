using System.Net;
using System.Net.Mail;
using System.Text;
using Markdig;
using Microsoft.Extensions.Options;
using CopilotDigest.Models;

namespace CopilotDigest.Services;

/// <summary>
/// Sends topic summaries to a configured e-mail address via SMTP.
/// Markdown returned by the LLM is converted to styled HTML before sending.
/// </summary>
public class EmailService : IEmailService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

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

        var subject = $"CopilotDigest – {DateTime.UtcNow:dddd, MMMM d yyyy}";
        var htmlBody = BuildHtmlEmail(summaries);

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_settings.Username, _settings.Password),
        };

        using var message = new MailMessage(_settings.FromAddress, _settings.ToAddress, subject, htmlBody)
        {
            IsBodyHtml = true,
        };

        _logger.LogInformation("Sending email to {To}", _settings.ToAddress);

        await client.SendMailAsync(message, cancellationToken);

        _logger.LogInformation("Email sent successfully.");
    }

    private static string BuildHtmlEmail(IReadOnlyList<SummaryResult> summaries)
    {
        var date = DateTime.UtcNow.ToString("dddd, MMMM d yyyy · HH:mm UTC");
        var topicsHtml = new StringBuilder();

        foreach (var result in summaries)
        {
            if (result.IsSuccess)
            {
                var contentHtml = Markdown.ToHtml(result.Summary, MarkdownPipeline);
                topicsHtml.Append("<tr><td style=\"padding:28px 32px;border-bottom:1px solid #e8e8e8;\">");
                topicsHtml.Append("<h2 style=\"margin:0 0 18px;color:#0969da;font-size:18px;font-weight:700;letter-spacing:-0.2px;\">");
                topicsHtml.Append(WebUtility.HtmlEncode(result.TopicName));
                topicsHtml.Append("</h2><div class=\"md\">");
                topicsHtml.Append(contentHtml);
                topicsHtml.Append("</div></td></tr>");
            }
            else
            {
                topicsHtml.Append("<tr><td style=\"padding:28px 32px;border-bottom:1px solid #e8e8e8;\">");
                topicsHtml.Append("<h2 style=\"margin:0 0 8px;color:#cf222e;font-size:18px;font-weight:700;\">");
                topicsHtml.Append(WebUtility.HtmlEncode(result.TopicName));
                topicsHtml.Append("</h2><p style=\"margin:0;color:#656d76;font-size:14px;\">Failed to generate summary: ");
                topicsHtml.Append(WebUtility.HtmlEncode(result.ErrorMessage ?? "unknown error"));
                topicsHtml.Append("</p></td></tr>");
            }
        }

        const string css =
            "body{margin:0;padding:0;background:#f4f4f4;font-family:Arial,Helvetica,sans-serif}" +
            ".md{color:#24292f;font-size:15px;line-height:1.7}" +
            ".md h1,.md h2{color:#0969da;margin:22px 0 8px;font-size:17px}" +
            ".md h3,.md h4{color:#24292f;margin:18px 0 6px;font-size:15px}" +
            ".md p{margin:8px 0}" +
            ".md ul,.md ol{padding-left:22px;margin:8px 0}" +
            ".md li{margin:5px 0}" +
            ".md strong{color:#24292f}" +
            ".md em{color:#656d76}" +
            ".md hr{border:none;border-top:1px solid #e8e8e8;margin:18px 0}" +
            ".md blockquote{border-left:4px solid #d0d7de;margin:12px 0;padding:6px 16px;color:#656d76;background:#f6f8fa;border-radius:0 4px 4px 0}" +
            ".md code{background:#f6f8fa;padding:2px 6px;border-radius:4px;font-size:13px;font-family:Consolas,monospace;color:#953800}" +
            ".md pre{background:#f6f8fa;padding:14px 16px;border-radius:6px;overflow-x:auto;font-size:13px}" +
            ".md pre code{background:none;padding:0;color:#24292f}" +
            ".md table{border-collapse:collapse;width:100%;margin:14px 0;font-size:14px}" +
            ".md th{background:#f6f8fa;padding:10px 14px;text-align:left;border:1px solid #d0d7de;font-weight:700}" +
            ".md td{padding:9px 14px;border:1px solid #d0d7de}" +
            ".md tr:nth-child(even) td{background:#fafafa}";

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head>");
        html.Append("<meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1.0\">");
        html.Append("<style>").Append(css).Append("</style>");
        html.Append("</head><body>");
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;background:#f4f4f4;\">");
        html.Append("<tr><td style=\"padding:28px 16px;\">");
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:700px;margin:0 auto;background:#ffffff;border-radius:10px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);\">");
        html.Append("<tr><td style=\"background:linear-gradient(135deg,#0969da,#0550ae);padding:28px 32px;\">");
        html.Append("<h1 style=\"margin:0;color:#ffffff;font-size:24px;font-weight:700;letter-spacing:-0.3px;\">CopilotDigest</h1>");
        html.Append("<p style=\"margin:6px 0 0;color:#b3d4ff;font-size:13px;\">").Append(date).Append("</p>");
        html.Append("</td></tr>");
        html.Append(topicsHtml);
        html.Append("<tr><td style=\"padding:16px 32px;background:#f6f8fa;border-top:1px solid #e8e8e8;\">");
        html.Append("<p style=\"margin:0;color:#aaa;font-size:11px;\">Delivered by CopilotDigest &middot; ").Append(date).Append("</p>");
        html.Append("</td></tr>");
        html.Append("</table></td></tr></table></body></html>");

        return html.ToString();
    }
}
