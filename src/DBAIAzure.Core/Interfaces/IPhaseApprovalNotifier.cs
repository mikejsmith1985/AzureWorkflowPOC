// Outbound seam: pushes the validation summary + gaps + portal link to the Forge decision card.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Pushes an approval request to the external decision card (Forge Terminal) when a run pauses
/// for review, so the reviewer does not need to be watching the portal. Implementations are
/// fire-and-forget from the orchestrator's perspective — failures are logged but never block the
/// run from entering its awaiting-approval state (mirrors the ticket pipeline's Teams notifier).
/// </summary>
public interface IPhaseApprovalNotifier
{
    /// <summary>
    /// Posts the run id, feature, phase, validation summary, flagged gaps, and a portal deep-link
    /// to the decision-card endpoint. The reviewer approves/rejects from the card, which calls
    /// back the approval webhook.
    /// </summary>
    Task NotifyAsync(
        string runId,
        string featureKey,
        SpecKitPhase phase,
        string summary,
        IReadOnlyList<PhaseValidationGap> gaps,
        string portalUrl,
        CancellationToken cancellationToken = default);
}
