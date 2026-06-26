// HITL notifier — sends pause-for-input notifications through the platform-agnostic Messaging delivery seam.
using System.Text;
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Integrations.Messaging;

/// <summary>
/// Notifies the responsible person when a pipeline run pauses for human input, delivering through
/// <see cref="IMessageDelivery"/> so the message reaches whichever platform (Teams/Slack/Discord) the
/// operator configured. Composition is platform-neutral plain text; the delivery seam applies the
/// per-platform formatting. Fire-and-forget: a delivery failure is logged and never thrown, so the run
/// still enters its paused state (FR-010).
/// </summary>
public sealed class MessagingHitlNotifier : IHitlNotifier
{
    private readonly IMessageDelivery _delivery;
    private readonly ILogger<MessagingHitlNotifier> _logger;

    public MessagingHitlNotifier(IMessageDelivery delivery, ILogger<MessagingHitlNotifier> logger)
    {
        _delivery = delivery;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task NotifyAsync(
        string runId,
        string ticketId,
        string title,
        IReadOnlyList<string> questions,
        string portalUrl,
        CancellationToken cancellationToken = default)
    {
        var message = ComposeMessage(runId, ticketId, title, questions, portalUrl);

        try
        {
            var result = await _delivery.SendAsync(message, cancellationToken);
            if (result.IsSuccess)
                _logger.LogInformation("HITL notification sent for run {RunId} via {Path} ({Platform})",
                    runId, result.Path, result.Platform);
            else
                _logger.LogWarning("HITL notification not delivered for run {RunId}: {Detail}", runId, result.Message);
        }
        catch (Exception ex)
        {
            // Defence in depth — delivery already swallows failures, but a notification must never fail the run.
            _logger.LogError(ex, "Failed to send HITL notification for run {RunId}", runId);
        }
    }

    /// <summary>Builds the plain-text notification: title, numbered questions, and a portal deep-link.</summary>
    private static string ComposeMessage(
        string runId, string ticketId, string title, IReadOnlyList<string> questions, string portalUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine();
        builder.AppendLine("A pipeline run needs your input:");
        for (var index = 0; index < questions.Count; index++)
            builder.AppendLine($"{index + 1}. {questions[index]}");
        builder.AppendLine();
        builder.AppendLine($"Respond here: {portalUrl}");
        builder.Append($"(run {runId} · ticket {ticketId})");
        return builder.ToString();
    }
}
