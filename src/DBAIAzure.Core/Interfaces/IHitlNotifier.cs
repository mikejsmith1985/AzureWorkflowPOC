namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Sends an external notification (e.g. Teams Adaptive Card) when the pipeline pauses
/// for human input, so the PO does not need to be watching the web UI.
/// </summary>
public interface IHitlNotifier
{
    /// <summary>
    /// Post a notification with the clarifying questions and a deep-link back to the portal run page.
    /// Implementations are fire-and-forget from the orchestrator's perspective —
    /// failures are logged but do not block the pipeline from entering AwaitingHuman state.
    /// </summary>
    Task NotifyAsync(
        string runId,
        string ticketId,
        string title,
        IReadOnlyList<string> questions,
        string portalUrl,
        CancellationToken cancellationToken = default);
}
