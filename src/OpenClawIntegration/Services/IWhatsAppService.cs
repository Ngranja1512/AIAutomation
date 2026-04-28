using OpenClawIntegration.Models;

namespace OpenClawIntegration.Services;

/// <summary>Sends topic summaries to a configured WhatsApp number.</summary>
public interface IWhatsAppService
{
    Task SendSummariesAsync(IReadOnlyList<SummaryResult> summaries, CancellationToken cancellationToken = default);
}
