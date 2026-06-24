// Outbound notification contract for human-approval gate events (FR-19, distinct from IHitlNotifier).

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Sends approval-request notifications when a workflow builder run suspends at a human-approval node.
/// This interface is distinct from <see cref="IHitlNotifier"/> (used by the ticket pipeline) —
/// different signature, different domain, different approver model (per FR-19 research R4).
/// Fire-and-forget from the orchestrator's perspective — failures are logged as non-fatal
/// and must never roll back the run state persist.
/// </summary>
public interface IWorkflowApprovalNotifier
{
    /// <summary>
    /// Sends an approval-request message to the first approver in the chain.
    /// </summary>
    /// <param name="runId">Identifies the run; embedded in the callback action payload.</param>
    /// <param name="workflowName">Display name of the suspended workflow.</param>
    /// <param name="nodeLabel">Label of the suspended approval node shown in the message.</param>
    /// <param name="question">Plain-language question from <c>ApprovalNodeConfig.PromptShown</c>.</param>
    /// <param name="approverChain">Ordered list of approver UPNs; first entry is notified.</param>
    /// <param name="decisionOptions">Labels for action buttons (e.g. ["Approve", "Reject"]).</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> approverChain,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct = default);

    /// <summary>
    /// Sends an escalation message to the next approver in the chain after a timeout.
    /// </summary>
    /// <param name="currentApproverIndex">Index of the approver who timed out.</param>
    Task EscalateAsync(
        string runId,
        string workflowName,
        string nodeLabel,
        string question,
        IReadOnlyList<string> approverChain,
        int currentApproverIndex,
        IReadOnlyList<string> decisionOptions,
        CancellationToken ct = default);
}
